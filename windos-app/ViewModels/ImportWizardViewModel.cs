using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using AgriGis.Desktop.Services.Import;

namespace AgriGis.Desktop.ViewModels;

// WB4 B407: ImportWizard の状態遷移ロジックを Form から分離した ViewModel。
// INotifyPropertyChanged により Form がプロパティ変化を監視。
// 副作用 (API 呼出, ファイル IO) は最小限、UI スレッド非依存でテスト可能 (B505)。
public sealed class ImportWizardViewModel : INotifyPropertyChanged
{
    private readonly IApiClient _api;

    public ImportWizardViewModel(IApiClient api)
    {
        _api = api;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- Step 1: Source 選択 ----
    private string _sourceFormat = "geojson";
    private string? _filePath;
    private int _lonColIndex;
    private int _latColIndex = 1;
    private int _sourceSrid = 4326;
    private string _layerName = "";

    public string SourceFormat { get => _sourceFormat; set => Set(ref _sourceFormat, value); }
    public string? FilePath { get => _filePath; set => Set(ref _filePath, value); }
    public int LonColIndex { get => _lonColIndex; set => Set(ref _lonColIndex, value); }
    public int LatColIndex { get => _latColIndex; set => Set(ref _latColIndex, value); }
    public int SourceSrid { get => _sourceSrid; set => Set(ref _sourceSrid, value); }
    public string LayerName { get => _layerName; set => Set(ref _layerName, value); }

    // ---- Step 2: スキーマ調整 ----
    public BindingList<InferredField> InferredFields { get; } = new();

    // ---- Step 3: 投入実行 ----
    private int _currentStep = 1;
    private bool _isImporting;
    private int _progress;
    private string? _lastError;
    private int? _createdLayerId;
    private Guid? _activeJobId;

    public int CurrentStep { get => _currentStep; private set => Set(ref _currentStep, value); }
    public bool IsImporting { get => _isImporting; private set => Set(ref _isImporting, value); }
    public int Progress { get => _progress; private set => Set(ref _progress, value); }
    public string? LastError { get => _lastError; private set => Set(ref _lastError, value); }
    public int? CreatedLayerId { get => _createdLayerId; private set => Set(ref _createdLayerId, value); }

    // ---- ガード ----
    public bool CanGoNext => CurrentStep switch
    {
        1 => !string.IsNullOrWhiteSpace(_layerName) && !string.IsNullOrEmpty(_filePath),
        2 => InferredFields.Count > 0,
        _ => false
    };
    public bool CanGoBack => CurrentStep > 1 && !_isImporting;

    // ---- 遷移 ----
    public void EnterNextStep()
    {
        if (!CanGoNext) return;
        if (CurrentStep < 3) CurrentStep++;
    }

    public void PreviousStep()
    {
        if (!CanGoBack) return;
        CurrentStep--;
    }

    // スキーマ推論 (Step1 → Step2 移行時に呼ぶ)
    public async Task LoadSchemaAsync(CancellationToken ct)
    {
        LastError = null;
        if (string.IsNullOrEmpty(_filePath)) { LastError = "FilePath is required"; return; }

        await using var source = CreateSource();
        var fields = await source.InferSchemaAsync(ct);
        InferredFields.Clear();
        foreach (var f in fields) InferredFields.Add(f);
    }

    // 投入実行 (Step3): create layer → start job → chunk × N → finalize
    public async Task ImportAsync(int chunkSize, CancellationToken ct)
    {
        if (IsImporting) return;
        if (string.IsNullOrEmpty(_filePath)) { LastError = "FilePath is required"; return; }

        IsImporting = true;
        Progress = 0;
        LastError = null;
        try
        {
            // 1. CreateLayer
            var schema = new LayerSchemaDto(
                InferredFields.Select(f => new SchemaFieldDto(f.Name, f.Type, f.Required, null)).ToList());
            var createReq = new CreateLayerRequestDto(
                LayerName: _layerName,
                LayerType: GuessLayerType(),
                GeometryType: GuessLayerType() switch
                {
                    "point" => "Point",
                    _ => "Point"
                },
                SourceFormat: _sourceFormat,
                SourceSrid: _sourceSrid,
                Description: null,
                Schema: schema);
            var layer = await _api.CreateLayerAsync(createReq, ct);
            CreatedLayerId = layer.LayerId;

            // 2. Start import job
            var job = await _api.StartImportJobAsync(layer.LayerId, new StartImportJobRequestDto(null), ct);
            _activeJobId = job.JobId;

            // 3. Read features → chunk × N
            int chunkOrdinal = 0;
            int total = 0;
            await using var source = CreateSource();
            await foreach (var chunk in Chunker.ChunkAsync(source.ReadFeaturesAsync(4326, ct), chunkSize, ct))
            {
                var items = chunk.Select(f => new BulkFeatureItemDto(f.Geometry, f.Properties)).ToList();
                var req = new BulkFeaturesRequestDto(
                    JobId: job.JobId,
                    ChunkOrdinal: chunkOrdinal,
                    ChunkTotal: -1, // 不明
                    SourceFormat: _sourceFormat,
                    Features: items);
                var res = await _api.BulkInsertFeaturesAsync(layer.LayerId, req, ct);
                total += res.InsertedCount;
                Progress = total;
                chunkOrdinal++;
            }

            // 4. Finalize succeeded
            await _api.FinalizeImportJobAsync(job.JobId, new FinalizeImportJobRequestDto("succeeded", null), ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            // failed finalize (best effort)
            if (_activeJobId is { } jid)
            {
                try
                {
                    await _api.FinalizeImportJobAsync(jid,
                        new FinalizeImportJobRequestDto("failed", ex.Message), CancellationToken.None);
                }
                catch { /* swallow */ }
            }
            throw;
        }
        finally
        {
            IsImporting = false;
        }
    }

    private ILayerSource CreateSource()
    {
        if (_filePath is null) throw new InvalidOperationException("FilePath is null");
        return _sourceFormat switch
        {
            "geojson" => new GeoJsonLayerSource(_filePath),
            "csv" => new CsvLayerSource(_filePath, _lonColIndex, _latColIndex, _sourceSrid),
            _ => throw new NotSupportedException($"Phase B では非対応形式: {_sourceFormat}")
        };
    }

    private string GuessLayerType() => _sourceFormat == "csv" ? "point" : "point";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        // ガードプロパティも変更通知 (派生プロパティ)
        if (name == nameof(CurrentStep) || name == nameof(LayerName) ||
            name == nameof(FilePath) || name == nameof(IsImporting))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoNext)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
        }
        return true;
    }
}
