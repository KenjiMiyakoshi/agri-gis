using AgriGis.Desktop.Services;
using AgriGis.Desktop.Services.Import;
using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.Srid;
using AgriGis.Desktop.ViewModels;
using Microsoft.Extensions.Options;

namespace AgriGis.Desktop.Forms;

// WB4 B408 / WC3 C401: 3 ステップウィザード。
// Step1: SourceFormat + ファイル選択 + 形式別 options (CSV / Shapefile)
// Step2: 推論済みスキーマの確認・調整 (SchemaGrid)
// Step3: 投入実行 (ProgressBar、キャンセル)
public partial class ImportWizardForm : Form
{
    private readonly ImportWizardViewModel _vm;
    private const int ChunkSize = 1000;

    // ComboBox items: ラベル → 内部 sourceFormat 値。FormatValue null は非活性表示。
    // (ValueTuple は ComboBox の DataSource バインドで PropertyDescriptor を作れないため
    //  通常のクラスで持つ)
    private sealed class FormatItem
    {
        public string Label { get; init; } = "";
        public string? FormatValue { get; init; }
        public override string ToString() => Label;
    }

    private static readonly FormatItem[] FormatItems =
    {
        new() { Label = "GeoJSON",                                   FormatValue = "geojson" },
        new() { Label = "CSV (lat/lng)",                             FormatValue = "csv" },
        new() { Label = "Shapefile ZIP",                             FormatValue = "shapefile" },
        // C'104 (WC'1): MIF/MID 解禁
        new() { Label = "MapInfo MIF/MID ZIP",                       FormatValue = "mif" },
        // C'204 (WC'2 予定): TAB は WC'2 で解禁
        new() { Label = "MapInfo TAB (Phase C' WC'2 対応予定)",      FormatValue = null },
    };

    public ImportWizardForm(IApiClient api,
        IEncodingResolver encodingResolver,
        IOptions<ImportOptions> importOptions)
    {
        _vm = new ImportWizardViewModel(api, encodingResolver, importOptions);
        InitializeComponent();
        WireEvents();
        UpdateButtons();
    }

    private void WireEvents()
    {
        // 形式選択 (ラベル + 内部値の (string,string?) tuple をそのまま Items に入れる)
        sourceFormatCombo.DataSource = FormatItems;
        sourceFormatCombo.DisplayMember = "Label";
        sourceFormatCombo.ValueMember = "FormatValue";
        sourceFormatCombo.SelectedIndex = 0;
        sourceFormatCombo.SelectedIndexChanged += (_, _) =>
        {
            if (sourceFormatCombo.SelectedItem is not FormatItem item) return;
            var fmt = item.FormatValue;
            if (fmt is null)
            {
                MessageBox.Show("この形式は Phase C' で対応予定です。", "AgriGis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                sourceFormatCombo.SelectedIndex = 0;
                return;
            }
            _vm.SourceFormat = fmt;
            csvOptionsGroup.Visible = fmt == "csv";
            shapefileOptionsGroup.Visible = fmt == "shapefile";
            shapefileInlinePanel.Visible = fmt == "shapefile";
            UpdateFileFilter();
        };

        browseButton.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = GetFilterForFormat() };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _vm.FilePath = dlg.FileName;
                filePathLabel.Text = dlg.FileName;
                // shapefile のときは選択直後に検出ボタンを使ってもらうため Inline panel を更新
                if (_vm.SourceFormat == "shapefile")
                {
                    detectStatusLabel.Text = "[自動検出] を押してください";
                }
                UpdateButtons();
            }
        };

        layerNameText.TextChanged += (_, _) => { _vm.LayerName = layerNameText.Text; UpdateButtons(); };

        // CSV options
        lonColText.TextChanged += (_, _) => { if (int.TryParse(lonColText.Text, out var v)) _vm.LonColIndex = v; };
        latColText.TextChanged += (_, _) => { if (int.TryParse(latColText.Text, out var v)) _vm.LatColIndex = v; };
        sridText.TextChanged += (_, _) => { if (int.TryParse(sridText.Text, out var v)) _vm.SourceSrid = v; };

        // Shapefile options: 検出ボタン + encoding 上書き + 手動 SRID
        detectButton.Click += async (_, _) => await DetectAsync();
        encodingCombo.Items.AddRange(new object[] { "(.cpg 自動)", "CP932", "UTF-8", "EUC-JP" });
        encodingCombo.SelectedIndex = 0;
        encodingCombo.SelectedIndexChanged += (_, _) =>
        {
            var sel = encodingCombo.SelectedItem?.ToString();
            _vm.EncodingOverride = (sel == null || sel.StartsWith("(")) ? null : sel;
        };
        manualSridText.TextChanged += (_, _) =>
        {
            _vm.ManualSridInput = int.TryParse(manualSridText.Text, out var v) ? v : null;
            UpdateButtons();
        };

        nextButton.Click += async (_, _) => await NextAsync();
        backButton.Click += (_, _) => { _vm.PreviousStep(); ShowStep(); };
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _vm.PropertyChanged += (_, e) =>
        {
            UpdateButtons();
            if (e.PropertyName == nameof(ImportWizardViewModel.DetectedEncoding) ||
                e.PropertyName == nameof(ImportWizardViewModel.DetectedSrid) ||
                e.PropertyName == nameof(ImportWizardViewModel.SridResolutionState) ||
                e.PropertyName == nameof(ImportWizardViewModel.FieldCount) ||
                e.PropertyName == nameof(ImportWizardViewModel.FeatureCount))
            {
                UpdateShapefileInline();
            }
        };
        schemaGrid.SetFields(_vm.InferredFields);

        // 初期状態
        csvOptionsGroup.Visible = false;
        shapefileOptionsGroup.Visible = false;
        shapefileInlinePanel.Visible = false;
    }

    private string GetFilterForFormat() => _vm.SourceFormat switch
    {
        "geojson" => "GeoJSON|*.geojson;*.json|All|*.*",
        "csv" => "CSV|*.csv|All|*.*",
        "shapefile" => "Shapefile ZIP|*.zip|All|*.*",
        "mif" => "MapInfo MIF/MID ZIP|*.zip|All|*.*",  // C'104 (WC'1)
        _ => "All|*.*"
    };

    private void UpdateFileFilter()
    {
        // OpenFileDialog は呼び出し時にだけ参照されるので、UI 側で表示だけ整える
        filePathHint.Text = (_vm.SourceFormat == "shapefile" || _vm.SourceFormat == "mif")
            ? "ZIP ファイル:" : "ファイル:";
    }

    private async Task DetectAsync()
    {
        try
        {
            detectStatusLabel.Text = "検出中...";
            await _vm.DetectShapefileAsync(CancellationToken.None);
            detectStatusLabel.Text = "検出完了";
            UpdateShapefileInline();
        }
        catch (Exception ex)
        {
            detectStatusLabel.Text = $"検出失敗: {ex.Message}";
        }
    }

    private void UpdateShapefileInline()
    {
        detectedEncodingLabel.Text = _vm.DetectedEncoding is null ? "未検出"
            : $"文字コード: {_vm.DetectedEncoding}";
        detectedSridLabel.Text = _vm.DetectedSrid is null ? "SRID: 未検出"
            : $"SRID: {_vm.DetectedSrid}";
        sridStateLabel.Text = _vm.SridResolutionState is null ? "" : $"  ({_vm.SridResolutionState})";
        countsLabel.Text = $"フィールド数: {_vm.FieldCount} / 形状数: {_vm.FeatureCount}";

        // FallbackToPrompt 時のみ手動 SRID 入力欄を強調
        manualSridText.Enabled = _vm.SridResolutionState == SridResolutionState.FallbackToPrompt
            || _vm.SridResolutionState == null;
    }

    private void ShowStep()
    {
        step1Panel.Visible = _vm.CurrentStep == 1;
        step2Panel.Visible = _vm.CurrentStep == 2;
        step3Panel.Visible = _vm.CurrentStep == 3;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        backButton.Enabled = _vm.CanGoBack;
        nextButton.Enabled = _vm.CanGoNext;
        nextButton.Text = _vm.CurrentStep < 3 ? "次へ >" : "投入";
        progressBar.Maximum = Math.Max(_vm.Progress, 100);
        progressBar.Value = Math.Min(_vm.Progress, progressBar.Maximum);
        progressLabel.Text = _vm.IsImporting ? $"投入中 {_vm.Progress}..." :
            (_vm.LastError is not null ? $"エラー: {_vm.LastError}" : "");
    }

    private async Task NextAsync()
    {
        try
        {
            if (_vm.CurrentStep == 1)
            {
                _vm.EnterNextStep();
                await _vm.LoadSchemaAsync(CancellationToken.None);
                ShowStep();
            }
            else if (_vm.CurrentStep == 2)
            {
                _vm.EnterNextStep();
                ShowStep();
                await _vm.ImportAsync(ChunkSize, CancellationToken.None);
                MessageBox.Show($"レイヤ作成成功 (layer_id={_vm.CreatedLayerId}, total={_vm.Progress})",
                    "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex is ApiException apiEx && apiEx.Problem.Errors.Count > 0)
            {
                msg += "\n\n詳細:\n" + string.Join("\n",
                    apiEx.Problem.Errors.Select(e => $"- {e.AttributeKey} [{e.Code}]: {e.Message}"));
            }
            MessageBox.Show($"失敗: {msg}", "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateButtons();
        }
    }
}
