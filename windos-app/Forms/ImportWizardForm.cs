using AgriGis.Desktop.Services;
using AgriGis.Desktop.ViewModels;

namespace AgriGis.Desktop.Forms;

// WB4 B408: 3 ステップウィザード。
// Step1: SourceFormat + ファイル選択 + (CSV) lon/lat 列確定 + SRID
// Step2: 推論済みスキーマの確認・調整 (SchemaGrid)
// Step3: 投入実行 (ProgressBar、キャンセル)
public partial class ImportWizardForm : Form
{
    private readonly ImportWizardViewModel _vm;
    private const int ChunkSize = 1000;

    public ImportWizardForm(IApiClient api)
    {
        _vm = new ImportWizardViewModel(api);
        InitializeComponent();
        WireEvents();
        UpdateButtons();
    }

    private void WireEvents()
    {
        // 形式選択 (geojson / csv のみ有効)
        sourceFormatCombo.Items.AddRange(new object[] { "geojson", "csv", "shapefile (Phase C)", "mif (Phase C)", "tab (Phase C)" });
        sourceFormatCombo.SelectedIndex = 0;
        sourceFormatCombo.SelectedIndexChanged += (_, _) =>
        {
            var selected = sourceFormatCombo.SelectedItem?.ToString() ?? "geojson";
            if (selected.Contains("Phase C"))
            {
                MessageBox.Show("この形式は Phase C で対応予定です。", "AgriGis",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                sourceFormatCombo.SelectedIndex = 0;
                return;
            }
            _vm.SourceFormat = selected;
            csvOptionsGroup.Visible = selected == "csv";
        };

        browseButton.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "GeoJSON / CSV|*.geojson;*.json;*.csv|All|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _vm.FilePath = dlg.FileName;
                filePathLabel.Text = dlg.FileName;
                UpdateButtons();
            }
        };

        layerNameText.TextChanged += (_, _) => { _vm.LayerName = layerNameText.Text; UpdateButtons(); };
        lonColText.TextChanged += (_, _) => { if (int.TryParse(lonColText.Text, out var v)) _vm.LonColIndex = v; };
        latColText.TextChanged += (_, _) => { if (int.TryParse(latColText.Text, out var v)) _vm.LatColIndex = v; };
        sridText.TextChanged += (_, _) => { if (int.TryParse(sridText.Text, out var v)) _vm.SourceSrid = v; };

        nextButton.Click += async (_, _) => await NextAsync();
        backButton.Click += (_, _) => { _vm.PreviousStep(); ShowStep(); };
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _vm.PropertyChanged += (_, _) => UpdateButtons();
        schemaGrid.SetFields(_vm.InferredFields);
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
            MessageBox.Show($"失敗: {ex.Message}", "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateButtons();
        }
    }
}
