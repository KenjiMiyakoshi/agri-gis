using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using AgriGis.Desktop.Services.Import;
using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.Packages;
using AgriGis.Desktop.Services.Import.Srid;
using Microsoft.Extensions.Options;

namespace AgriGis.Desktop.ViewModels;

// WB4 B407: ImportWizard の状態遷移ロジックを Form から分離した ViewModel。
// WC3 C401 拡張: Shapefile 対応 + inline 検出プロパティ (DetectedEncoding /
// DetectedSrid / SridResolutionState / FieldCount / FeatureCount)。
//
// 副作用 (API 呼出, ファイル IO, OGR) は本クラスに集約、UI スレッド非依存テスト可能 (B505/C504)。
public sealed class ImportWizardViewModel : INotifyPropertyChanged
{
    private readonly IApiClient _api;
    private readonly IEncodingResolver? _encodingResolver;
    private readonly IOptions<ImportOptions>? _importOptions;

    // テスト容易性のため、Shapefile 専用 deps は省略可能。
    public ImportWizardViewModel(IApiClient api)
    {
        _api = api;
    }

    public ImportWizardViewModel(
        IApiClient api,
        IEncodingResolver encodingResolver,
        IOptions<ImportOptions> importOptions)
    {
        _api = api;
        _encodingResolver = encodingResolver;
        _importOptions = importOptions;
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

    // ---- WC3 C401: Shapefile 検出プロパティ (inline 表示用) ----
    private string? _detectedEncoding;
    private int? _detectedSrid;
    private SridResolutionState? _sridState;
    private int _fieldCount;
    private int _featureCount;
    private string? _encodingOverride;
    private int? _manualSridInput;

    public string? DetectedEncoding { get => _detectedEncoding; private set => Set(ref _detectedEncoding, value); }
    public int? DetectedSrid { get => _detectedSrid; private set => Set(ref _detectedSrid, value); }
    public SridResolutionState? SridResolutionState { get => _sridState; private set => Set(ref _sridState, value); }
    public int FieldCount { get => _fieldCount; private set => Set(ref _fieldCount, value); }
    public int FeatureCount { get => _featureCount; private set => Set(ref _featureCount, value); }
    public string? EncodingOverride { get => _encodingOverride; set => Set(ref _encodingOverride, value); }
    public int? ManualSridInput { get => _manualSridInput; set => Set(ref _manualSridInput, value); }

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
        1 => !string.IsNullOrWhiteSpace(_layerName) && !string.IsNullOrEmpty(_filePath) && IsSridReadyForStep1,
        2 => InferredFields.Count > 0,
        _ => false
    };
    public bool CanGoBack => CurrentStep > 1 && !_isImporting;

    // shapefile 専用: SRID 検出未完了 / Rejected で Next 不可。
    // GeoJSON / CSV は SourceSrid 直入力なので常に OK。
    private bool IsSridReadyForStep1
    {
        get
        {
            if (_sourceFormat != "shapefile") return true;
            return _sridState switch
            {
                Services.Import.Srid.SridResolutionState.Detected => true,
                Services.Import.Srid.SridResolutionState.FallbackToWgs84 => true,
                Services.Import.Srid.SridResolutionState.FallbackToPrompt => _manualSridInput.HasValue,
                Services.Import.Srid.SridResolutionState.Rejected => false,
                _ => false  // 未検出
            };
        }
    }

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

    // WC3 C401: Shapefile 自動検出 (Step1 の [自動検出] ボタンから呼ぶ)
    public async Task DetectShapefileAsync(CancellationToken ct)
    {
        LastError = null;
        if (string.IsNullOrEmpty(_filePath))
        {
            LastError = "FilePath is required";
            return;
        }
        if (_encodingResolver is null || _importOptions is null)
        {
            LastError = "Shapefile detection requires encoding/options dependencies";
            return;
        }

        ShapefilePackage? package = null;
        try
        {
            package = await ShapefilePackage.OpenAsync(_filePath, ct);

            // 1. 文字コード解決 (UI 上書き優先)
            var encoding = !string.IsNullOrEmpty(_encodingOverride)
                ? _encodingOverride
                : _encodingResolver.Resolve(package);
            DetectedEncoding = encoding;

            // 2. SRID 検出 (手動入力があればそちらを優先)
            var detector = _manualSridInput.HasValue
                ? (ISridDetector)new ManualSridDetector(_manualSridInput.Value)
                : new OgrSridDetector(_importOptions);
            var sridResult = await detector.DetectAsync(package, ct);
            DetectedSrid = sridResult.Srid;
            SridResolutionState = sridResult.State;
            if (sridResult.Srid is { } s) _sourceSrid = s;

            // 3. スキーマ + feature 件数 (実 OGR Open)
            var encResolverFn = new InlineEncodingResolver(encoding);
            var srcPackage = package;
            package = null!;  // GdalLayerSource が引き継ぐので外側 Dispose 不要
            await using var src = new GdalLayerSource(srcPackage, detector, encResolverFn, "shapefile");
            var fields = await src.InferSchemaAsync(ct);
            InferredFields.Clear();
            foreach (var f in fields) InferredFields.Add(f);
            FieldCount = fields.Count;

            int featCount = 0;
            await foreach (var _ in src.ReadFeaturesAsync(4326, ct))
            {
                featCount++;
                if (featCount >= 100_000) break;
            }
            FeatureCount = featCount;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            if (package is not null) await package.DisposeAsync();
            NotifyGuardChanged();
        }
    }

    // スキーマ推論 (Step1 → Step2 移行時に呼ぶ)
    public async Task LoadSchemaAsync(CancellationToken ct)
    {
        LastError = null;
        if (string.IsNullOrEmpty(_filePath)) { LastError = "FilePath is required"; return; }

        if (_sourceFormat == "shapefile")
        {
            // shapefile は DetectShapefileAsync で既に推論済 (InferredFields に格納済)。
            if (InferredFields.Count == 0)
            {
                await DetectShapefileAsync(ct);
            }
            return;
        }

        await using var source = await CreateSourceAsync(ct);
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
                GeometryType: GuessGeometryType(),
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
            await using var source = await CreateSourceAsync(ct);
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

    private async ValueTask<ILayerSource> CreateSourceAsync(CancellationToken ct)
    {
        if (_filePath is null) throw new InvalidOperationException("FilePath is null");
        switch (_sourceFormat)
        {
            case "geojson":
                return new GeoJsonLayerSource(_filePath);
            case "csv":
                return new CsvLayerSource(_filePath, _lonColIndex, _latColIndex, _sourceSrid);
            case "shapefile":
                if (_encodingResolver is null || _importOptions is null)
                    throw new InvalidOperationException("Shapefile import requires encoding/options dependencies");
                var pkg = await ShapefilePackage.OpenAsync(_filePath, ct);
                var encResolver = !string.IsNullOrEmpty(_encodingOverride)
                    ? (IEncodingResolver)new InlineEncodingResolver(_encodingOverride!)
                    : _encodingResolver;
                var sridDetector = _manualSridInput.HasValue
                    ? (ISridDetector)new ManualSridDetector(_manualSridInput.Value)
                    : new OgrSridDetector(_importOptions);
                return new GdalLayerSource(pkg, sridDetector, encResolver, "shapefile");
            case "mif":
                // C'102 + C'103 + C'104 (WC'1): MIF/MID 対応
                if (_encodingResolver is null || _importOptions is null)
                    throw new InvalidOperationException("MIF import requires encoding/options dependencies");
                var mifPkg = await MifPackage.OpenAsync(_filePath, ct);
                var mifEncResolver = !string.IsNullOrEmpty(_encodingOverride)
                    ? (IEncodingResolver)new InlineEncodingResolver(_encodingOverride!)
                    : _encodingResolver;
                var mifSridDetector = _manualSridInput.HasValue
                    ? (ISridDetector)new ManualSridDetector(_manualSridInput.Value)
                    : new OgrSridDetector(_importOptions);
                return new GdalLayerSource(mifPkg, mifSridDetector, mifEncResolver, "mif");
            case "tab":
                // C'201 + C'204 (WC'2): TAB 対応
                if (_encodingResolver is null || _importOptions is null)
                    throw new InvalidOperationException("TAB import requires encoding/options dependencies");
                var tabPkg = await TabPackage.OpenAsync(_filePath, ct);
                var tabEncResolver = !string.IsNullOrEmpty(_encodingOverride)
                    ? (IEncodingResolver)new InlineEncodingResolver(_encodingOverride!)
                    : _encodingResolver;
                var tabSridDetector = _manualSridInput.HasValue
                    ? (ISridDetector)new ManualSridDetector(_manualSridInput.Value)
                    : new OgrSridDetector(_importOptions);
                return new GdalLayerSource(tabPkg, tabSridDetector, tabEncResolver, "tab");
            default:
                throw new NotSupportedException($"Unsupported format: {_sourceFormat}");
        }
    }

    private string GuessLayerType() => _sourceFormat switch
    {
        "csv" => "point",
        "shapefile" => "polygon",
        _ => "point"
    };

    private string GuessGeometryType() => _sourceFormat switch
    {
        "csv" => "Point",
        "shapefile" => "MultiPolygon",
        _ => "Point"
    };

    private void NotifyGuardChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoNext)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(CurrentStep) || name == nameof(LayerName) ||
            name == nameof(FilePath) || name == nameof(IsImporting) ||
            name == nameof(SridResolutionState) || name == nameof(ManualSridInput))
        {
            NotifyGuardChanged();
        }
        return true;
    }

    // 内部用: UI 上書き encoding を反映するためのインライン IEncodingResolver。
    private sealed class InlineEncodingResolver : IEncodingResolver
    {
        private readonly string _encoding;
        public InlineEncodingResolver(string encoding) { _encoding = encoding; }
        public string Resolve(IImportPackage package) => _encoding;
    }
}
