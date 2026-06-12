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
        // F301 (Phase F WF3): layerList (CheckedListBox) の ItemCheck で layer_visibility_change を通知
        layerList.ItemCheck += OnLayerListItemCheck;
        // F'304 (Phase F' WF'3): layerList drag-and-drop で z-order 並べ替え
        layerList.MouseDown += OnLayerListMouseDown;
        layerList.MouseMove += OnLayerListMouseMove;
        layerList.DragOver += OnLayerListDragOver;
        layerList.DragDrop += OnLayerListDragDrop;
        // F'304 hotfix: ドラッグ中の視覚フィードバック (ghost form + cursor 変更)
        layerList.GiveFeedback += OnLayerListGiveFeedback;
        attributeEditor.Saved += OnAttributeEditorSaved;
        attributeEditor.FeatureLoaded += (_, _) => ApplyGuestRestriction();
        // E402 (WE4): asOf 過去時点モード切替
        asOfEnabled.CheckedChanged += OnAsOfEnabledChanged;
        asOfPicker.ValueChanged += OnAsOfPickerChanged;
        // WB4 B406: レイヤ管理メニュー
        // F305 (Phase F WF3): admin の場合だけ「権限管理...」ボタンを表示
        layerAdminMenuItem.Click += async (_, _) =>
        {
            using (var f = _sp.GetRequiredService<LayerAdminForm>())
            {
                f.SetAdminVisibility(_session.Current?.IsAdmin ?? false);
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

            // F'305 (Phase F' WF'3): 永続化された layer 順序を適用
            await ApplyPersistedLayerOrderSafelyAsync();

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

    // F302 (Phase F WF3): read-only 判定を 3 条件 OR に拡張
    //   - guest user
    //   - asOf 過去時点モード
    //   - 現在ロード中の feature の layer が canEdit=false
    private void ApplyGuestRestriction()
    {
        var isGuest = _session.Current?.IsGuest ?? false;
        var inPastMode = asOfEnabled.Checked;
        var currentLayerCantEdit = !_canEditCurrent;
        attributeEditor.SetReadOnly(isGuest || inPastMode || currentLayerCantEdit);
    }

    // F302: 現在 AttributeEditor に表示中の feature の layer の canEdit 状態 (true = 編集可)
    // HandleFeaturesSelectedAsync で feature ロード時に Controller.GetLayerById で更新する。
    private bool _canEditCurrent = true;

    // WB5 fix: LayerAdminForm からのインポート/削除後に呼ぶ、または起動時の初期読込で呼ぶ。
    // F301 (Phase F WF3): layerList ベース。VisibleLayerIds は Controller が保持。
    private async Task ReloadLayersAsync()
    {
        try
        {
            SetStatus("Loading layers...");
            // F301: prevSelectedLayerId は廃止 (複数選択時代に「直前の単一選択」概念がなくなる)。
            //       VisibleLayerIds は Controller 内に永続化されるので、ReloadAsync(null) で OK。
            var result = await _controller.ReloadAsync(prevSelectedLayerId: null, CancellationToken.None);
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

    // F'304 (Phase F' WF'3): CheckedListBox の item として LayerDto をラップ
    private sealed class LayerListItem
    {
        public LayerDto Layer { get; }
        public LayerListItem(LayerDto l) { Layer = l; }
        public override string ToString() => $"{Layer.LayerId}: {Layer.LayerName} ({Layer.LayerType})";
    }

    // F301 (Phase F WF3): CheckedListBox を再構築 + VisibleLayerIds に従って初期 check 状態を復元。
    // F'304: 表示順は OrderedLayerIds (checked 部分の z-order) + 残り (unchecked、API 順)。
    private bool _suppressItemCheck;
    private void ApplyReloadResult(ReloadResult result)
    {
        _suppressItemCheck = true;
        try
        {
            layerList.Items.Clear();
            var checkedSet = new HashSet<int>(_controller.OrderedLayerIds);
            // 1) Ordered (checked) layers in z-order
            foreach (var lid in _controller.OrderedLayerIds)
            {
                var l = _controller.GetLayerById(lid);
                if (l is not null) layerList.Items.Add(new LayerListItem(l), true);
            }
            // 2) Remaining (unchecked) layers in API order
            foreach (var l in result.Layers)
            {
                if (!checkedSet.Contains(l.LayerId))
                {
                    layerList.Items.Add(new LayerListItem(l), false);
                }
            }
        }
        finally
        {
            _suppressItemCheck = false;
        }
        if (result.Layers.Count == 0)
        {
            SetStatus("No layers");
            return;
        }
        // F301: 初期 ON の layer を WebGIS にも送る (Controller 側で先頭 1 件が初期 ON 済)
        foreach (var lid in _controller.OrderedLayerIds)
        {
            _bridge?.Send("layer_visibility_change", new { layerId = lid, visible = true });
        }
        // F'304: 初期 z-order も WebGIS に送る
        if (_controller.OrderedLayerIds.Count > 0)
        {
            _bridge?.Send("layer_order_change", new { layerIds = _controller.OrderedLayerIds.ToArray() });
        }
        SetStatus($"{result.Layers.Count} layers ({_controller.VisibleLayerIds.Count} visible)");
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

    // F301 (Phase F WF3): CheckedListBox の ItemCheck で layer_visibility_change を bridge 経由で WebGIS に通知。
    // CheckedListBox.ItemCheck は **状態変更前** に発火するので、NewValue から visible を取る。
    // F'305 (Phase F' WF'3): check 変更後に SaveLayerOrderAsync で永続化
    private void OnLayerListItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheck) return;
        if (e.Index < 0 || e.Index >= layerList.Items.Count) return;
        if (layerList.Items[e.Index] is not LayerListItem item) return;
        var layer = item.Layer;
        var visible = e.NewValue == CheckState.Checked;
        _controller.SetLayerVisible(layer.LayerId, visible);
        _bridge?.Send("layer_visibility_change", new { layerId = layer.LayerId, visible });
        // F'305: 永続化 (best-effort、失敗は status bar のみ)
        _ = SaveLayerOrderSafelyAsync();
        SetStatus($"Layer {layer.LayerId} {(visible ? "ON" : "OFF")} ({_controller.VisibleLayerIds.Count} visible)");
    }

    // ====== F'304 (Phase F' WF'3): drag-and-drop で z-order 並べ替え ======
    private int _dragSourceIndex = -1;
    private Point _dragStartPoint;
    // F'304 hotfix: ドラッグ中のゴースト (マウス追従表示)
    private DragGhostForm? _dragGhost;

    // 半透明ゴースト Form (borderless / TopMost / non-activating)
    private sealed class DragGhostForm : Form
    {
        public Label TextLabel { get; }
        public DragGhostForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Opacity = 0.85;
            BackColor = Color.LightYellow;
            TextLabel = new Label
            {
                AutoSize = true,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.LightYellow,
                ForeColor = Color.Black
            };
            Controls.Add(TextLabel);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    private void OnLayerListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragSourceIndex = layerList.IndexFromPoint(e.Location);
        _dragStartPoint = e.Location;
    }

    private void OnLayerListMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragSourceIndex < 0) return;
        // SystemInformation.DragSize の閾値を越えたら drag 開始 (click と区別)
        var dx = Math.Abs(e.X - _dragStartPoint.X);
        var dy = Math.Abs(e.Y - _dragStartPoint.Y);
        if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;

        // F'304 hotfix: ゴースト Form を表示してドラッグ中のレイヤ名をマウスに追従させる
        if (_dragSourceIndex < layerList.Items.Count &&
            layerList.Items[_dragSourceIndex] is LayerListItem li)
        {
            _dragGhost ??= new DragGhostForm();
            _dragGhost.TextLabel.Text = $"↕  {li.Layer.LayerName}";
            _dragGhost.Size = new Size(
                _dragGhost.TextLabel.PreferredWidth + 4,
                _dragGhost.TextLabel.PreferredHeight + 4);
            _dragGhost.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 14);
            _dragGhost.Show();
        }
        try
        {
            layerList.DoDragDrop(_dragSourceIndex, DragDropEffects.Move);
        }
        finally
        {
            _dragGhost?.Hide();
            _dragSourceIndex = -1;
        }
    }

    private void OnLayerListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(int)) != true) return;
        e.Effect = DragDropEffects.Move;
        // F'304 hotfix: カーソル下の項目を選択ハイライトして drop 位置を視覚化
        var point = layerList.PointToClient(new Point(e.X, e.Y));
        var idx = layerList.IndexFromPoint(point);
        if (idx >= 0 && idx < layerList.Items.Count && layerList.SelectedIndex != idx)
        {
            layerList.SelectedIndex = idx;
        }
    }

    // F'304 hotfix: ドラッグ中のカーソル変更 + ゴースト Form をマウスに追従
    // GiveFeedback は DoDragDrop の modal loop 内で連続発火する
    private void OnLayerListGiveFeedback(object? sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Cursor.Current = Cursors.Hand;
        if (_dragGhost is { Visible: true })
        {
            _dragGhost.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 14);
        }
    }

    private void OnLayerListDragDrop(object? sender, DragEventArgs e)
    {
        var src = (int)(e.Data?.GetData(typeof(int)) ?? -1);
        if (src < 0 || src >= layerList.Items.Count) return;
        var point = layerList.PointToClient(new Point(e.X, e.Y));
        var dst = layerList.IndexFromPoint(point);
        if (dst < 0) dst = layerList.Items.Count - 1;
        if (src == dst) return;

        _suppressItemCheck = true;
        try
        {
            var item = layerList.Items[src];
            var wasChecked = layerList.GetItemChecked(src);
            layerList.Items.RemoveAt(src);
            layerList.Items.Insert(dst, item);
            layerList.SetItemChecked(dst, wasChecked);
            layerList.SelectedIndex = dst;
        }
        finally
        {
            _suppressItemCheck = false;
        }

        // 新しい z-order を controller に反映 + WebGIS / 永続化に通知
        var newOrder = new List<int>();
        for (int i = 0; i < layerList.Items.Count; i++)
        {
            if (layerList.GetItemChecked(i) &&
                layerList.Items[i] is LayerListItem li)
            {
                newOrder.Add(li.Layer.LayerId);
            }
        }
        try
        {
            _controller.ReorderLayers(newOrder);
            _bridge?.Send("layer_order_change", new { layerIds = newOrder.ToArray() });
            _ = SaveLayerOrderSafelyAsync();
            SetStatus($"Reordered: {string.Join(',', newOrder)}");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus($"reorder failed: {ex.Message}");
        }
    }

    // F'305: 永続化 (best-effort)
    private async Task SaveLayerOrderSafelyAsync()
    {
        try
        {
            await _controller.SaveLayerOrderAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus($"save layer order failed: {ex.Message}");
        }
    }

    // F'305: 起動時に永続化された order を適用 (best-effort、失敗時は API 順のまま)
    private async Task ApplyPersistedLayerOrderSafelyAsync()
    {
        try
        {
            var persisted = await _controller.LoadLayerOrderAsync(CancellationToken.None);
            if (persisted is null || persisted.Count == 0) return;
            _controller.ApplyPersistedLayerOrder(persisted);
            // CheckedListBox を再構築して新 order を反映
            ApplyReloadResult(new ReloadResult(_controller.Layers, 0));
        }
        catch (Exception ex)
        {
            SetStatus($"load layer order failed: {ex.Message}");
        }
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
            // F302: 該当 layer の canEdit を引いて AttributeEditor の read-only 制御に反映
            // GetLayerById が null (まだロード前等) は安全側で canEdit=false 扱い
            _canEditCurrent = _controller.GetLayerById(layerId)?.CanEdit ?? false;
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
