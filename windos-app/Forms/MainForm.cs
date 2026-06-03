using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using AgriGis.Desktop.ViewModels;
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
    // E'203 (WE'2): asOf 状態を AsOfState クラスに委譲
    private readonly AsOfState _asOf = new();
    // H5-101 (WH5-1): MainForm から layers 管理 + Unauthorized 復旧経路を切り出し
    private readonly MainFormController _controller;

    public MainForm(IApiClient api, ISessionStore session, IServiceProvider sp)
    {
        _api = api;
        _session = session;
        _sp = sp;
        _controller = new MainFormController(api, session, _asOf);
        InitializeComponent();
        // WB4 B405 (H4 解消): AttributeEditorControl に IFeatureSaveCoordinator を注入
        attributeEditor.SetCoordinator(this);
        layerCombo.SelectedIndexChanged += OnLayerComboChanged;
        attributeEditor.Saved += OnAttributeEditorSaved;
        attributeEditor.FeatureLoaded += (_, _) => ApplyGuestRestriction();
        // E402 (WE4): asOf 過去時点モード切替
        asOfEnabled.CheckedChanged += OnAsOfEnabledChanged;
        asOfPicker.ValueChanged += OnAsOfPickerChanged;
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
    // H5-101 (WH5-1): Controller 経由で layer 取得 + ComboBox 更新は MainForm に残す。
    private async Task ReloadLayersAsync()
    {
        try
        {
            SetStatus("Loading layers...");
            var layers = _controller.Layers;
            var prevLayerId = layerCombo.SelectedIndex >= 0 && layerCombo.SelectedIndex < layers.Count
                ? (int?)layers[layerCombo.SelectedIndex].LayerId
                : null;

            var result = await _controller.ReloadAsync(prevLayerId, CancellationToken.None);
            ApplyReloadResult(result);
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

    private void ApplyReloadResult(ReloadResult result)
    {
        layerCombo.Items.Clear();
        foreach (var l in result.Layers)
        {
            layerCombo.Items.Add($"{l.LayerId}: {l.LayerName} ({l.LayerType})");
        }
        if (result.Layers.Count == 0)
        {
            SetStatus("No layers");
            return;
        }
        layerCombo.SelectedIndex = result.RestoreIndex;
        SetStatus($"{result.Layers.Count} layers");
    }

    // H5-102 (WH5-1): Unauthorized 復旧経路を Controller 経由に統合 (旧版の reload 二重実装を解消)
    private async Task HandleUnauthorizedAsync()
    {
        Hide();
        var loginSucceeded = _controller.TryRecoverUnauthorizedAsync(
            showLoginAndReturnSuccess: () =>
            {
                using var login = _sp.GetRequiredService<LoginForm>();
                return login.ShowDialog() == DialogResult.OK;
            },
            CancellationToken.None);
        var ok = await loginSucceeded;
        if (!ok)
        {
            Close();
            return;
        }
        Show();
        // Controller が再 reload した結果を ComboBox に反映 (prev = null で先頭選択)
        ApplyReloadResult(new ReloadResult(_controller.Layers, _controller.Layers.Count > 0 ? 0 : -1));
        ApplyGuestRestriction();
        SetStatus("Re-authenticated");
    }

    private void OnLayerComboChanged(object? sender, EventArgs e)
    {
        var layers = _controller.Layers;
        if (_bridge is null || layerCombo.SelectedIndex < 0 || layerCombo.SelectedIndex >= layers.Count)
        {
            return;
        }
        var layer = layers[layerCombo.SelectedIndex];
        _bridge.Send("layer_select", new { layerId = layer.LayerId });
        SetStatus($"Layer {layer.LayerId} selected");
    }

    // D401 (WD4): bridge handler を features_selected (entityIds 配列) 受領に書き換え。
    // 単数モード (entityIds.Length == 1) は既存の LoadFeature、複数モードは LoadFeatures (D402)。
    // selection_overlay_ready / theme_change の受領も追加 (現状はステータスバーログのみ)。
    private async void OnBridgeMessage(object? sender, Envelope envelope)
    {
        try
        {
            if (envelope.Type == "features_selected")
            {
                await HandleFeaturesSelectedAsync(envelope.Payload);
            }
            else if (envelope.Type == "selection_overlay_ready")
            {
                if (envelope.Payload.TryGetProperty("count", out var countProp) &&
                    envelope.Payload.TryGetProperty("sid", out var sidProp))
                {
                    SetStatus($"Selection overlay ready: {countProp.GetInt32()} entities (sid={sidProp.GetString()})");
                }
            }
        }
        catch (UnauthorizedApiException)
        {
            await HandleUnauthorizedAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"bridge message failed ({envelope.Type}): {ex.Message}");
        }
    }

    private async Task HandleFeaturesSelectedAsync(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("entityIds", out var entityIdsProp) ||
            entityIdsProp.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        var entityIds = new List<Guid>(entityIdsProp.GetArrayLength());
        foreach (var idProp in entityIdsProp.EnumerateArray())
        {
            var s = idProp.GetString();
            if (Guid.TryParse(s, out var g)) entityIds.Add(g);
        }

        if (entityIds.Count == 0) return;

        if (entityIds.Count == 1)
        {
            // 単数モード: 既存 LoadFeature 経路
            var entityId = entityIds[0];
            var feature = await _api.GetFeatureAsync(entityId, asOf: null, CancellationToken.None);
            var layerId = feature.Properties.LayerId;
            var schemaRes = await _api.GetLayerSchemaAsync(layerId, _asOf.Current, CancellationToken.None);
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
        else
        {
            // N 件モード (D402)
            if (InvokeRequired)
            {
                Invoke(new Action(() => attributeEditor.LoadFeatures(entityIds)));
            }
            else
            {
                attributeEditor.LoadFeatures(entityIds);
            }
            SetStatus($"{entityIds.Count} features selected (multi-mode)");
        }
    }

    // D401 (WD4): theme 切替を WebGIS に送る (theme_change envelope)
    // 現時点では UI 配置を伴わないため public method として外部から呼べる形で残置。
    // WD4 後の UI 仕上げ (theme ComboBox 追加) は朝のレビュー時に判断。
    public void SendThemeChange(int layerId, string theme)
    {
        _bridge?.Send("theme_change", new { layerId, theme });
    }

    // E402 (WE4): asOf 過去時点モード切替
    // E'203 (WE'2): AsOfState 経由に書き換え、ApiClient 呼び出しにも _asOf.Current を伝搬
    private void OnAsOfEnabledChanged(object? sender, EventArgs e)
    {
        asOfPicker.Enabled = asOfEnabled.Checked;
        if (asOfEnabled.Checked)
        {
            _asOf.SetEnabled(true, DateOnly.FromDateTime(asOfPicker.Value));
        }
        else
        {
            _asOf.Disable();
        }
        var asOf = asOfEnabled.Checked ? asOfPicker.Value.ToString("yyyy-MM-dd") : null;
        _bridge?.Send("asof_change", new { asOf });
        // 過去時点モード中は属性編集 disable (Phase E: 過去時点の更新は不可)
        attributeEditor.SetReadOnly(asOfEnabled.Checked);
        SetStatus(asOfEnabled.Checked
            ? $"過去時点モード ({asOf}): 編集不可"
            : "現在モード: 編集可能");
    }

    private void OnAsOfPickerChanged(object? sender, EventArgs e)
    {
        if (!asOfEnabled.Checked) return;
        _asOf.SetValue(DateOnly.FromDateTime(asOfPicker.Value));
        var asOf = asOfPicker.Value.ToString("yyyy-MM-dd");
        _bridge?.Send("asof_change", new { asOf });
        SetStatus($"過去時点モード ({asOf}): 編集不可");
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
