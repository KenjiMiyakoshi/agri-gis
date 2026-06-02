using System.Text.Json;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.Forms;

public partial class AttributeEditorControl : UserControl
{
    // 保存成功時に layerId を渡して親フォームに通知 (features_reload 用)
    public event EventHandler<int>? Saved;
    // A404: feature が読み込まれた直後に発火。MainForm が guest UI 制限を再適用するため。
    public event EventHandler? FeatureLoaded;

    private LayerSchema? _schema;
    private FeatureDto? _feature;
    private readonly Dictionary<string, Control> _fieldControls = new();
    private bool _readOnly;
    private IFeatureSaveCoordinator? _coordinator;

    // WB4 B405 (H4 解消): MainForm 直接キャストを廃止し、コーディネータ経由で API 呼び出し
    public void SetCoordinator(IFeatureSaveCoordinator coordinator) => _coordinator = coordinator;

    // A404: guest 等の閲覧専用ユーザは保存ボタンを無効化する
    public void SetReadOnly(bool readOnly)
    {
        _readOnly = readOnly;
        if (_feature is not null) saveButton.Enabled = !readOnly;
    }

    public AttributeEditorControl()
    {
        InitializeComponent();
        saveButton.Click += async (_, _) => await SaveAsync();
    }

    public void LoadFeature(LayerSchema schema, FeatureDto feature)
    {
        _schema = schema;
        _feature = feature;
        errorLabel.Text = "";

        headerLabel.Text = $"Entity {feature.Properties.EntityId} (v{feature.Properties.Version})";
        BuildFields(schema, feature);
        saveButton.Enabled = !_readOnly;
        FeatureLoaded?.Invoke(this, EventArgs.Empty);
    }

    private const int LabelWidth = 110;
    private const int RowHeight = 28;
    private const int RowMargin = 4;

    private void BuildFields(LayerSchema schema, FeatureDto feature)
    {
        fieldsLayout.SuspendLayout();
        fieldsLayout.Controls.Clear();
        _fieldControls.Clear();

        // 行幅は fieldsLayout のクライアント幅から VScroll 分を引いた値を使う
        int rowWidth = Math.Max(200, fieldsLayout.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);

        var descriptors = SchemaFormBuilder.Build(schema);
        foreach (var d in descriptors)
        {
            var rowPanel = new Panel
            {
                Width = rowWidth,
                Height = RowHeight,
                Margin = new Padding(0, 0, 0, RowMargin)
            };

            var label = new Label
            {
                Text = d.Required ? $"{d.Label} *" : d.Label,
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(LabelWidth, RowHeight),
                AutoSize = false,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            Control input = CreateControlForKind(d.Kind);
            input.Location = new System.Drawing.Point(LabelWidth + 4, (RowHeight - GetNaturalHeight(input)) / 2);
            input.Width = rowWidth - LabelWidth - 8;
            input.Tag = d.Key;
            _fieldControls[d.Key] = input;

            // 既存値があれば反映
            if (feature.Properties.Attributes.TryGetValue(d.Key, out var existing))
            {
                ApplyValue(input, d.Kind, existing);
            }

            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(input);
            fieldsLayout.Controls.Add(rowPanel);
        }
        fieldsLayout.ResumeLayout();
    }

    // single-line 系コントロールの自然高さを返す。垂直中央配置の Y 計算に使う。
    private static int GetNaturalHeight(Control control) => control switch
    {
        CheckBox => 18,
        _        => control.Height > 0 ? control.Height : 23
    };

    private static Control CreateControlForKind(FieldKind kind) => kind switch
    {
        FieldKind.Number  => new NumericUpDown { DecimalPlaces = 2, Minimum = decimal.MinValue / 2, Maximum = decimal.MaxValue / 2 },
        FieldKind.Integer => new NumericUpDown { DecimalPlaces = 0, Minimum = int.MinValue,        Maximum = int.MaxValue },
        FieldKind.Boolean => new CheckBox(),
        FieldKind.Date    => new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" },
        _                 => new TextBox()
    };

    private static void ApplyValue(Control control, FieldKind kind, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return;
        switch (kind)
        {
            case FieldKind.Number when control is NumericUpDown nd && value.TryGetDecimal(out var dec):
                nd.Value = dec;
                break;
            case FieldKind.Integer when control is NumericUpDown ni && value.TryGetInt64(out var i):
                ni.Value = i;
                break;
            case FieldKind.Boolean when control is CheckBox cb && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False):
                cb.Checked = value.GetBoolean();
                break;
            case FieldKind.Date when control is DateTimePicker dp && value.ValueKind == JsonValueKind.String:
                if (DateTime.TryParse(value.GetString(), out var dt)) dp.Value = dt;
                break;
            default:
                if (control is TextBox tb) tb.Text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
                break;
        }
    }

    private async Task SaveAsync()
    {
        if (_schema is null || _feature is null) return;

        var attrs = CollectValues();
        var errors = AttributeValidator.Validate(_schema, attrs);
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

        try
        {
            saveButton.Enabled = false;
            errorLabel.Text = "Saving...";

            // WB4 B405 (H4 解消): MainForm 直接キャストの代わりに IFeatureSaveCoordinator 経由
            var coord = _coordinator
                       ?? throw new InvalidOperationException("IFeatureSaveCoordinator was not set");

            var entityId = Guid.Parse(_feature.Properties.EntityId);
            var req = new UpdateFeatureRequestDto(null, attrs);
            var result = await coord.UpdateFeatureAsync(
                entityId, req, _feature.Properties.Version, CancellationToken.None);

            errorLabel.Text = $"Saved. New version: {result.Version}";
            // 親に features_reload 要求
            Saved?.Invoke(this, _feature.Properties.LayerId);

            // 新 version で feature を取り直し
            _feature = await coord.GetFeatureAsync(entityId, CancellationToken.None);
            headerLabel.Text = $"Entity {entityId} (v{_feature.Properties.Version})";
        }
        catch (ApiException apiEx) when (apiEx.Status == 409)
        {
            MessageBox.Show(
                "他のユーザが先に保存しました。地図を再読込してください。",
                "Version conflict",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (ApiException apiEx) when (apiEx.Status == 422)
        {
            ShowErrors(apiEx.Problem.Errors);
        }
        catch (Exception ex)
        {
            errorLabel.Text = ex.Message;
        }
        finally
        {
            saveButton.Enabled = true;
        }
    }

    private Dictionary<string, JsonElement> CollectValues()
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, control) in _fieldControls)
        {
            object boxed = control switch
            {
                TextBox tb => tb.Text,
                NumericUpDown nd when nd.DecimalPlaces == 0 => (long)nd.Value,
                NumericUpDown nd => (double)nd.Value,
                CheckBox cb => cb.Checked,
                DateTimePicker dp => dp.Value.ToString("yyyy-MM-dd"),
                _ => string.Empty
            };
            var json = JsonSerializer.Serialize(boxed);
            dict[key] = JsonDocument.Parse(json).RootElement.Clone();
        }
        return dict;
    }

    private void ShowErrors(IReadOnlyList<AttributeError> errors)
    {
        errorLabel.Text = string.Join(Environment.NewLine,
            errors.Select(e => $"- {e.AttributeKey}: {e.Message}"));
    }
}
