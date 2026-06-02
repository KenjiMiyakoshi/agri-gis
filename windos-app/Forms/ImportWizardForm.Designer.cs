#nullable enable

using AgriGis.Desktop.Controls;

namespace AgriGis.Desktop.Forms;

partial class ImportWizardForm
{
    private System.ComponentModel.IContainer? components = null;

    // Step 1 共通
    private Panel step1Panel = null!;
    private Label sourceFormatLabel = null!;
    private ComboBox sourceFormatCombo = null!;
    private Label filePathHint = null!;
    private Button browseButton = null!;
    private Label filePathLabel = null!;
    private Label layerNameLabel = null!;
    private TextBox layerNameText = null!;

    // Step 1 CSV options
    private GroupBox csvOptionsGroup = null!;
    private Label lonColHint = null!;
    private TextBox lonColText = null!;
    private Label latColHint = null!;
    private TextBox latColText = null!;
    private Label sridHint = null!;
    private TextBox sridText = null!;

    // Step 1 Shapefile options (WC3 C401)
    private GroupBox shapefileOptionsGroup = null!;
    private Button detectButton = null!;
    private Label encodingComboHint = null!;
    private ComboBox encodingCombo = null!;
    private Label manualSridHint = null!;
    private TextBox manualSridText = null!;
    private Label detectStatusLabel = null!;

    // Step 1 Shapefile inline 表示パネル (WC3 C401)
    private Panel shapefileInlinePanel = null!;
    private Label detectedEncodingLabel = null!;
    private Label detectedSridLabel = null!;
    private Label sridStateLabel = null!;
    private Label countsLabel = null!;

    // Step 2
    private Panel step2Panel = null!;
    private Label step2Title = null!;
    private SchemaGrid schemaGrid = null!;

    // Step 3
    private Panel step3Panel = null!;
    private Label step3Title = null!;
    private ProgressBar progressBar = null!;
    private Label progressLabel = null!;

    // 下部
    private Panel buttonPanel = null!;
    private Button backButton = null!;
    private Button nextButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // Step 1 共通
        step1Panel = new Panel { Dock = DockStyle.Fill };
        sourceFormatLabel = new Label { Text = "形式:", Location = new(16, 16), Size = new(80, 24) };
        sourceFormatCombo = new ComboBox { Location = new(100, 14), Size = new(360, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        filePathHint = new Label { Text = "ファイル:", Location = new(16, 56), Size = new(80, 24) };
        browseButton = new Button { Text = "参照...", Location = new(100, 52), Size = new(80, 26) };
        filePathLabel = new Label { Location = new(190, 56), Size = new(480, 24), AutoEllipsis = true };
        layerNameLabel = new Label { Text = "レイヤ名:", Location = new(16, 96), Size = new(80, 24) };
        layerNameText = new TextBox { Location = new(100, 94), Size = new(280, 24) };

        // CSV options
        csvOptionsGroup = new GroupBox
        {
            Text = "CSV オプション", Location = new(16, 130), Size = new(660, 70), Visible = false
        };
        lonColHint = new Label { Text = "lon 列 idx:", Location = new(12, 28), Size = new(80, 24), Parent = csvOptionsGroup };
        lonColText = new TextBox { Text = "0", Location = new(96, 26), Size = new(60, 24), Parent = csvOptionsGroup };
        latColHint = new Label { Text = "lat 列 idx:", Location = new(170, 28), Size = new(80, 24), Parent = csvOptionsGroup };
        latColText = new TextBox { Text = "1", Location = new(254, 26), Size = new(60, 24), Parent = csvOptionsGroup };
        sridHint = new Label { Text = "SRID:", Location = new(330, 28), Size = new(48, 24), Parent = csvOptionsGroup };
        sridText = new TextBox { Text = "4326", Location = new(380, 26), Size = new(80, 24), Parent = csvOptionsGroup };

        // Shapefile options (WC3 C401)
        shapefileOptionsGroup = new GroupBox
        {
            Text = "Shapefile オプション", Location = new(16, 130), Size = new(660, 70), Visible = false
        };
        detectButton = new Button { Text = "自動検出", Location = new(12, 26), Size = new(90, 26), Parent = shapefileOptionsGroup };
        encodingComboHint = new Label { Text = "文字コード:", Location = new(112, 28), Size = new(72, 24), Parent = shapefileOptionsGroup };
        encodingCombo = new ComboBox { Location = new(186, 26), Size = new(120, 24), DropDownStyle = ComboBoxStyle.DropDownList, Parent = shapefileOptionsGroup };
        manualSridHint = new Label { Text = "手動 SRID:", Location = new(316, 28), Size = new(76, 24), Parent = shapefileOptionsGroup };
        manualSridText = new TextBox { Location = new(394, 26), Size = new(80, 24), Parent = shapefileOptionsGroup };
        detectStatusLabel = new Label { Location = new(484, 28), Size = new(168, 24), Parent = shapefileOptionsGroup, AutoEllipsis = true };

        // Shapefile inline 表示 (WC3 C401)
        shapefileInlinePanel = new Panel { Location = new(16, 208), Size = new(660, 80), Visible = false, BorderStyle = BorderStyle.FixedSingle };
        detectedEncodingLabel = new Label { Location = new(8, 8), Size = new(320, 20), Parent = shapefileInlinePanel };
        detectedSridLabel = new Label { Location = new(8, 28), Size = new(320, 20), Parent = shapefileInlinePanel };
        sridStateLabel = new Label { Location = new(330, 28), Size = new(320, 20), Parent = shapefileInlinePanel };
        countsLabel = new Label { Location = new(8, 50), Size = new(640, 20), Parent = shapefileInlinePanel };

        step1Panel.Controls.AddRange(new Control[]
        {
            sourceFormatLabel, sourceFormatCombo,
            filePathHint, browseButton, filePathLabel,
            layerNameLabel, layerNameText,
            csvOptionsGroup,
            shapefileOptionsGroup,
            shapefileInlinePanel
        });

        // Step 2
        step2Panel = new Panel { Dock = DockStyle.Fill, Visible = false };
        step2Title = new Label { Text = "推論済みスキーマを確認・調整してください", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        schemaGrid = new SchemaGrid { Dock = DockStyle.Fill };
        step2Panel.Controls.Add(schemaGrid);
        step2Panel.Controls.Add(step2Title);

        // Step 3
        step3Panel = new Panel { Dock = DockStyle.Fill, Visible = false };
        step3Title = new Label { Text = "投入実行", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new System.Drawing.Font("Yu Gothic UI", 11, System.Drawing.FontStyle.Bold) };
        progressBar = new ProgressBar { Location = new(16, 50), Size = new(660, 24), Maximum = 100 };
        progressLabel = new Label { Location = new(16, 86), Size = new(660, 48), TextAlign = ContentAlignment.TopLeft };
        step3Panel.Controls.AddRange(new Control[] { step3Title, progressBar, progressLabel });

        // 下部ボタン
        buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        backButton = new Button { Text = "< 戻る", Location = new(420, 12), Size = new(80, 26) };
        nextButton = new Button { Text = "次へ >", Location = new(510, 12), Size = new(80, 26) };
        cancelButton = new Button { Text = "キャンセル", Location = new(600, 12), Size = new(80, 26) };
        buttonPanel.Controls.AddRange(new Control[] { backButton, nextButton, cancelButton });

        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(700, 460);
        Controls.Add(step1Panel);
        Controls.Add(step2Panel);
        Controls.Add(step3Panel);
        Controls.Add(buttonPanel);
        Name = "ImportWizardForm";
        Text = "レイヤインポート";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = nextButton;
        CancelButton = cancelButton;
    }
}
