using System.Text.Json;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using Microsoft.Web.WebView2.Core;

namespace AgriGis.Desktop.Forms;

public partial class MainForm : Form
{
    private const string WebGisUrl = "http://localhost:5173";

    private readonly IApiClient _api;
    private BridgeMessenger? _bridge;
    private IReadOnlyList<LayerDto> _layers = Array.Empty<LayerDto>();

    public MainForm(IApiClient api)
    {
        _api = api;
        InitializeComponent();
        layerCombo.SelectedIndexChanged += OnLayerComboChanged;
        attributeEditor.Saved += OnAttributeEditorSaved;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            SetStatus("WebView2 initializing...");
            await webView.EnsureCoreWebView2Async();

            // NavigationCompleted を 1 回だけ拾うための TCS
            var navTcs = new TaskCompletionSource();
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (_, _) =>
            {
                webView.CoreWebView2.NavigationCompleted -= handler!;
                navTcs.TrySetResult();
            };
            webView.CoreWebView2.NavigationCompleted += handler;

            webView.Source = new Uri(WebGisUrl);

            SetStatus("Loading WebGIS...");
            await navTcs.Task;

            // ここから初めて bridge 通信を許可
            _bridge = new BridgeMessenger(webView.CoreWebView2);
            _bridge.MessageReceived += OnBridgeMessage;

            // レイヤ一覧を取得して ComboBox に流す
            SetStatus("Loading layers...");
            _layers = await _api.GetLayersAsync(CancellationToken.None);
            layerCombo.Items.Clear();
            foreach (var l in _layers)
            {
                layerCombo.Items.Add($"{l.LayerId}: {l.LayerName} ({l.LayerType})");
            }
            if (_layers.Count > 0)
            {
                layerCombo.SelectedIndex = 0;
            }

            SetStatus("Ready");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 initialization failed:\n{ex.Message}",
                "AgriGis",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnLayerComboChanged(object? sender, EventArgs e)
    {
        if (_bridge is null || layerCombo.SelectedIndex < 0 || layerCombo.SelectedIndex >= _layers.Count)
        {
            return;
        }
        var layer = _layers[layerCombo.SelectedIndex];
        _bridge.Send("layer_select", new { layerId = layer.LayerId });
        SetStatus($"Layer {layer.LayerId} selected");
    }

    private async void OnBridgeMessage(object? sender, Envelope envelope)
    {
        if (envelope.Type != "feature_clicked")
        {
            return;
        }

        try
        {
            if (!envelope.Payload.TryGetProperty("entityId", out var entityProp))
            {
                return;
            }
            var entityIdStr = entityProp.GetString();
            if (string.IsNullOrEmpty(entityIdStr) || !Guid.TryParse(entityIdStr, out var entityId))
            {
                return;
            }

            var feature = await _api.GetFeatureAsync(entityId, asOf: null, CancellationToken.None);
            var layerId = feature.Properties.LayerId;
            var schemaRes = await _api.GetLayerSchemaAsync(layerId, CancellationToken.None);

            // API DTO → Core DTO へ詰め替え
            var coreSchema = new LayerSchema(
                schemaRes.Schema.Fields
                    .Select(f => new SchemaField(f.Key, f.Type, f.Required, f.Label))
                    .ToArray());

            if (InvokeRequired)
            {
                Invoke(new Action(() => attributeEditor.LoadFeature(coreSchema, feature)));
            }
            else
            {
                attributeEditor.LoadFeature(coreSchema, feature);
            }
            SetStatus($"Feature {entityId} loaded");
        }
        catch (Exception ex)
        {
            SetStatus($"feature_clicked failed: {ex.Message}");
        }
    }

    private void OnAttributeEditorSaved(object? sender, int layerId)
    {
        _bridge?.Send("features_reload", new { layerId });
    }

    private void SetStatus(string text)
    {
        if (statusStrip.InvokeRequired)
        {
            statusStrip.Invoke(new Action(() => statusLabel.Text = text));
        }
        else
        {
            statusLabel.Text = text;
        }
    }

    internal IApiClient Api => _api;
}
