using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace AgriGis.Desktop.Forms;

public partial class MainForm : Form, IFeatureSaveCoordinator
{
    private const string WebGisUrl = "http://localhost:5173";

    private readonly IApiClient _api;
    private readonly ISessionStore _session;
    private readonly IServiceProvider _sp;
    private BridgeMessenger? _bridge;
    private IReadOnlyList<LayerDto> _layers = Array.Empty<LayerDto>();

    public MainForm(IApiClient api, ISessionStore session, IServiceProvider sp)
    {
        _api = api;
        _session = session;
        _sp = sp;
        InitializeComponent();
        // WB4 B405 (H4 解消): AttributeEditorControl に IFeatureSaveCoordinator を注入
        attributeEditor.SetCoordinator(this);
        layerCombo.SelectedIndexChanged += OnLayerComboChanged;
        attributeEditor.Saved += OnAttributeEditorSaved;
        attributeEditor.FeatureLoaded += (_, _) => ApplyGuestRestriction();
        // WB4 B406: レイヤ管理メニュー
        layerAdminMenuItem.Click += async (_, _) =>
        {
            using (var f = _sp.GetRequiredService<LayerAdminForm>())
            {
                f.ShowDialog(this);
            }
            // 追加/削除が走った可能性があるので戻ってきたら一覧を再読込
            await ReloadLayersAsync();
        };
        // admin 以外で Visible=false (サーバの RequireRole と 2 重防御)
        layerAdminMenuItem.Visible = _session.Current?.IsAdmin ?? false;
    }

    // IFeatureSaveCoordinator: AttributeEditorControl から呼ばれる
    public Task<PatchFeatureResultDto> UpdateFeatureAsync(
        Guid entityId, UpdateFeatureRequestDto req, int ifMatchVersion, CancellationToken ct)
        => _api.UpdateFeatureAsync(entityId, req, ifMatchVersion, ct);

    public Task<FeatureDto> GetFeatureAsync(Guid entityId, CancellationToken ct)
        => _api.GetFeatureAsync(entityId, asOf: null, ct);

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

            // WebGIS は独立した HTTP クライアントなので、API 呼び出し前に JWT を渡す
            // (Phase A の WebGIS 認証は本来 Phase B 対応。動作確認用の最小実装)
            var session = _session.Current;
            if (session is not null)
            {
                _bridge.Send("auth_token", new { accessToken = session.AccessToken });
            }

            await ReloadLayersAsync();

            SetStatus("Ready");
            ApplyGuestRestriction();
        }
        catch (UnauthorizedApiException)
        {
            await HandleUnauthorizedAsync();
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

    private void ApplyGuestRestriction()
    {
        var isGuest = _session.Current?.IsGuest ?? false;
        attributeEditor.SetReadOnly(isGuest);
    }

    // WB5 fix: LayerAdminForm からのインポート/削除後に呼ぶ、または起動時の初期読込で呼ぶ。
    // 現在選択中の layer_id を保持して再選択を試みる。
    private async Task ReloadLayersAsync()
    {
        try
        {
            SetStatus("Loading layers...");
            var prevLayerId = layerCombo.SelectedIndex >= 0 && layerCombo.SelectedIndex < _layers.Count
                ? (int?)_layers[layerCombo.SelectedIndex].LayerId
                : null;

            _layers = await _api.GetLayersAsync(CancellationToken.None);
            layerCombo.Items.Clear();
            foreach (var l in _layers)
            {
                layerCombo.Items.Add($"{l.LayerId}: {l.LayerName} ({l.LayerType})");
            }
            if (_layers.Count == 0)
            {
                SetStatus("No layers");
                return;
            }

            var restoreIndex = prevLayerId is { } pid
                ? _layers.ToList().FindIndex(l => l.LayerId == pid)
                : -1;
            layerCombo.SelectedIndex = restoreIndex >= 0 ? restoreIndex : 0;
            SetStatus($"{_layers.Count} layers");
        }
        catch (UnauthorizedApiException)
        {
            await HandleUnauthorizedAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"layer reload failed: {ex.Message}");
        }
    }

    private async Task HandleUnauthorizedAsync()
    {
        _session.Clear();
        Hide();
        using var login = _sp.GetRequiredService<LoginForm>();
        var ok = login.ShowDialog() == DialogResult.OK;
        if (!ok)
        {
            Close();
            return;
        }
        Show();
        // 認証復旧後にレイヤを再取得
        try
        {
            _layers = await _api.GetLayersAsync(CancellationToken.None);
            layerCombo.Items.Clear();
            foreach (var l in _layers)
            {
                layerCombo.Items.Add($"{l.LayerId}: {l.LayerName} ({l.LayerType})");
            }
            if (_layers.Count > 0) layerCombo.SelectedIndex = 0;
            ApplyGuestRestriction();
            SetStatus("Re-authenticated");
        }
        catch (Exception ex)
        {
            SetStatus($"reload after login failed: {ex.Message}");
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
        catch (UnauthorizedApiException)
        {
            await HandleUnauthorizedAsync();
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
