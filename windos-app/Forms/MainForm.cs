using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Core.LayerTree;
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
        // LG303 (Phase LG WLG3): layerTree (owner-draw TreeView) のイベント配線。
        // 旧 layerList (DragAwareCheckedListBox) は LayerTreeView に置換済み。
        layerTree.LayerVisibleToggled += OnLayerVisibleToggled;
        layerTree.LayerEditToggled += layerId => OnLayerFlagToggled(layerId, isEdit: true);
        layerTree.LayerSnapToggled += layerId => OnLayerFlagToggled(layerId, isEdit: false);
        layerTree.GroupVisibleToggled += OnGroupVisibleToggled;
        layerTree.NodeMoved += OnLayerTreeNodeMoved;
        layerTree.LayersMoved += OnLayerTreeLayersMoved;
        layerTree.AfterExpand += OnLayerTreeExpandedChanged;
        layerTree.AfterCollapse += OnLayerTreeExpandedChanged;
        layerTree.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) layerTree.SelectedNode = e.Node;
        };
        layerTree.ContextMenuStrip = BuildLayerTreeContextMenu();
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

            // LG303: ReloadAsync が layer_tree_v1 / layer_flags_v1 / 旧 layer_order_v1 移行まで担う
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
    // LG303: Controller が tree (groups + preference マージ) を再構築する。
    private async Task ReloadLayersAsync()
    {
        try
        {
            SetStatus("Loading layers...");
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

    // LG303: Controller の LayerTreeModel から TreeView を再構築 + 初期 visibility/z-order を WebGIS へ送出
    private void ApplyReloadResult(ReloadResult result)
    {
        RebuildLayerTree();
        if (result.Layers.Count == 0)
        {
            SetStatus("No layers");
            return;
        }
        // 初期 ON の layer を WebGIS にも送る (Controller 側で先頭 1 件が初期 ON 済)
        foreach (var lid in _controller.OrderedLayerIds)
        {
            _bridge?.Send("layer_visibility_change", new { layerId = lid, visible = true });
        }
        // 初期 z-order も WebGIS に送る
        SendLayerOrderChange();
        SetStatus($"{result.Layers.Count} layers ({_controller.OrderedLayerIds.Count} visible)");
    }

    // ====== LG303: LayerTreeModel → TreeView 同期 ======

    // ツリー再構築中の AfterExpand/AfterCollapse による保存を抑止
    private bool _suppressTreeEvents;

    private void RebuildLayerTree()
    {
        _suppressTreeEvents = true;
        layerTree.BeginUpdate();
        try
        {
            layerTree.Nodes.Clear();
            AddTreeNodes(layerTree.Nodes, _controller.Tree.RootNodes);
        }
        finally
        {
            layerTree.EndUpdate();
            _suppressTreeEvents = false;
        }
    }

    private void AddTreeNodes(TreeNodeCollection dest, IReadOnlyList<LayerTreeNode> children)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case TreeGroupNode group:
                {
                    var node = new TreeNode(group.Name) { Tag = group };
                    dest.Add(node);
                    AddTreeNodes(node.Nodes, group.Children);
                    // Expanded 復元 (子を追加した後でないと Expand が効かない)
                    if (group.Expanded) node.Expand();
                    break;
                }
                case TreeLayerNode layer:
                {
                    var dto = _controller.GetLayerById(layer.LayerId);
                    var text = dto is null
                        ? $"layer {layer.LayerId}"
                        : $"{dto.LayerId}: {dto.LayerName} ({dto.LayerType})";
                    dest.Add(new TreeNode(text) { Tag = layer });
                    break;
                }
            }
        }
    }

    private void SendLayerOrderChange()
    {
        _bridge?.Send("layer_order_change", new { layerIds = _controller.OrderedLayerIds.ToArray() });
    }

    // ====== LG303: checkbox トグル ======

    // 表示 toggle: model 更新 → layer_visibility_change + layer_order_change → 永続化 (best-effort)
    private void OnLayerVisibleToggled(int layerId)
    {
        var current = _controller.Tree.FindLayer(layerId)?.Visible ?? false;
        var visible = !current;
        _controller.SetLayerVisible(layerId, visible);
        _bridge?.Send("layer_visibility_change", new { layerId, visible });
        SendLayerOrderChange();
        layerTree.Invalidate();
        _ = SaveTreeSafelyAsync();
        SetStatus($"Layer {layerId} {(visible ? "ON" : "OFF")} ({_controller.OrderedLayerIds.Count} visible)");
    }

    // グループ表示 toggle (3 値): Checked → 全 OFF、Unchecked/Mixed → 全 ON。
    // 変化した layer 分の layer_visibility_change ×N + layer_order_change ×1 を送る。
    private void OnGroupVisibleToggled(string key)
    {
        GroupCheckState state;
        try
        {
            state = _controller.GetGroupCheckState(key);
        }
        catch (KeyNotFoundException)
        {
            return; // stale key (再構築直前の連打等) は無視
        }
        var visible = state != GroupCheckState.Checked;
        var before = new HashSet<int>(_controller.OrderedLayerIds);
        _controller.SetGroupVisible(key, visible);
        var after = new HashSet<int>(_controller.OrderedLayerIds);
        var changed = visible ? after.Except(before) : before.Except(after);
        foreach (var layerId in changed)
        {
            _bridge?.Send("layer_visibility_change", new { layerId, visible });
        }
        SendLayerOrderChange();
        layerTree.Invalidate();
        _ = SaveTreeSafelyAsync();
        SetStatus($"Group {(visible ? "ON" : "OFF")} ({_controller.OrderedLayerIds.Count} visible)");
    }

    // 編集/スナップ toggle: 状態保存のみ (機能配線は将来サイクル)。layer_flags_v1 に永続化。
    private void OnLayerFlagToggled(int layerId, bool isEdit)
    {
        var node = _controller.Tree.FindLayer(layerId);
        if (node is null) return;
        if (isEdit)
        {
            _controller.SetLayerFlags(layerId, edit: !node.EditEnabled);
        }
        else
        {
            _controller.SetLayerFlags(layerId, snap: !node.SnapEnabled);
        }
        layerTree.Invalidate();
        _ = SaveFlagsSafelyAsync();
    }

    // ====== LG303: drag-and-drop 移動 ======

    // 単一ノード移動 (group は MoveGroup、layer は MoveLayersTo 単一に寄せる)。
    private void OnLayerTreeNodeMoved(object? sender, LayerTreeNodeMovedEventArgs e)
    {
        try
        {
            if (e.LayerId is int layerId)
            {
                // 単一 layer もまとめ移動ラッパ経由に統一 (Tree を直接触らせない設計に合わせる)
                _controller.MoveLayersTo(new[] { layerId }, e.TargetParentKey, e.TargetIndex);
            }
            else if (e.GroupKey is { } groupKey)
            {
                _controller.MoveGroup(groupKey, e.TargetParentKey, e.TargetIndex);
            }
            else
            {
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            SetStatus($"move failed: {ex.Message}");
            return;
        }
        RebuildLayerTree();
        SendLayerOrderChange();
        _ = SaveTreeSafelyAsync();
        SetStatus("Layer tree updated");
    }

    // LGP302: 複数 layer のまとめ移動。Core MoveLayers で原子的に並べ替え、
    // 再構築後に移動した layer 群の選択を復元する。
    private void OnLayerTreeLayersMoved(object? sender, LayerTreeLayersMovedEventArgs e)
    {
        if (e.LayerIds.Count == 0) return;
        try
        {
            _controller.MoveLayersTo(e.LayerIds, e.TargetParentKey, e.StartOrder);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            SetStatus($"move failed: {ex.Message}");
            return;
        }
        RebuildLayerTree();
        // 移動した layer 群を選択状態のまま維持する (再構築で TreeNode が作り直されるため復元)
        layerTree.RestoreSelectionByLayerIds(e.LayerIds);
        SendLayerOrderChange();
        _ = SaveTreeSafelyAsync();
        SetStatus($"{e.LayerIds.Count} layers moved");
    }

    // ====== LG303: 展開状態の永続化 ======

    private void OnLayerTreeExpandedChanged(object? sender, TreeViewEventArgs e)
    {
        if (_suppressTreeEvents) return;
        if (e.Node?.Tag is not TreeGroupNode group) return;
        _controller.SetGroupExpanded(group.Key, e.Node.IsExpanded);
        _ = SaveTreeSafelyAsync();
    }

    // ====== LG303: 右クリックメニュー (グループ作成 / 名変更 / 削除) ======

    private ContextMenuStrip BuildLayerTreeContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();
            var groupTag = layerTree.SelectedNode?.Tag as TreeGroupNode;
            var isAdmin = _session.Current?.IsAdmin ?? false;
            // グループ上で右クリック → その配下に作成、それ以外 → ルート直下
            var parentKey = groupTag?.Key;

            menu.Items.Add(new ToolStripMenuItem("グループ作成 (自分用)", null,
                (_, _) => CreateUserGroup(parentKey)));
            if (isAdmin)
            {
                // db: グループのみ親にできる (usr: 親を選択中なら DB 上はルート直下に作る)
                var dbParentId = ParseDbGroupId(parentKey);
                menu.Items.Add(new ToolStripMenuItem("デフォルトグループ作成 (admin)", null,
                    async (_, _) => await CreateDbGroupAsync(dbParentId)));
            }

            if (groupTag is not null)
            {
                var isUsr = groupTag.Key.StartsWith("usr:", StringComparison.Ordinal);
                // usr: は本人がいつでも編集可。db: の rename/削除は admin のみメニュー表示
                // (サーバの RequireRole と 2 重防御)
                if (isUsr || isAdmin)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(new ToolStripMenuItem("グループ名変更...", null,
                        async (_, _) => await RenameGroupAsync(groupTag)));
                    menu.Items.Add(new ToolStripMenuItem("グループ削除", null,
                        async (_, _) => await DeleteGroupAsync(groupTag)));
                }
            }
            e.Cancel = menu.Items.Count == 0;
        };
        return menu;
    }

    private static int? ParseDbGroupId(string? key)
        => key is not null && key.StartsWith("db:", StringComparison.Ordinal) &&
           int.TryParse(key.AsSpan(3), out var id)
            ? id
            : null;

    // usr: グループ作成 (pref のみ、他ユーザに影響なし)
    private void CreateUserGroup(string? parentKey)
    {
        var name = PromptForName("グループ作成 (自分用)", "");
        if (name is null) return;
        _controller.CreateUserGroup(name, parentKey);
        RebuildLayerTree();
        _ = SaveTreeSafelyAsync();
        SetStatus($"グループ「{name}」を作成しました");
    }

    // db: グループ作成 (admin のみ。成功後 Reload でツリーに反映)
    private async Task CreateDbGroupAsync(int? parentGroupId)
    {
        var name = PromptForName("デフォルトグループ作成 (全ユーザ共有)", "");
        if (name is null) return;
        try
        {
            await _api.CreateLayerGroupAsync(
                new CreateLayerGroupRequestDto(name, parentGroupId, null), CancellationToken.None);
            await ReloadLayersAsync();
            SetStatus($"デフォルトグループ「{name}」を作成しました");
        }
        catch (Exception ex)
        {
            SetStatus($"group create failed: {ex.Message}");
        }
    }

    private async Task RenameGroupAsync(TreeGroupNode group)
    {
        var name = PromptForName("グループ名変更", group.Name);
        if (name is null || name == group.Name) return;
        if (group.Key.StartsWith("usr:", StringComparison.Ordinal))
        {
            _controller.RenameUserGroup(group.Key, name);
            RebuildLayerTree();
            _ = SaveTreeSafelyAsync();
            SetStatus($"グループ名を「{name}」に変更しました");
            return;
        }
        // db: グループは admin API (成功後 Reload で全体反映、名前は DB 優先)
        var groupId = ParseDbGroupId(group.Key);
        if (groupId is null) return;
        try
        {
            await _api.UpdateLayerGroupAsync(groupId.Value,
                new UpdateLayerGroupRequestDto(name, null, null), CancellationToken.None);
            await ReloadLayersAsync();
            SetStatus($"グループ名を「{name}」に変更しました");
        }
        catch (Exception ex)
        {
            SetStatus($"group rename failed: {ex.Message}");
        }
    }

    private async Task DeleteGroupAsync(TreeGroupNode group)
    {
        var isUsr = group.Key.StartsWith("usr:", StringComparison.Ordinal);
        var detail = isUsr
            ? "中のレイヤと子グループは親へ移動します。"
            : "全ユーザのデフォルトツリーから削除されます。中のレイヤはルート直下へ移動します。";
        var answer = MessageBox.Show(this,
            $"グループ「{group.Name}」を削除しますか?\n{detail}",
            "グループ削除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        if (isUsr)
        {
            try
            {
                _controller.RemoveGroup(group.Key);
            }
            catch (KeyNotFoundException)
            {
                return;
            }
            RebuildLayerTree();
            _ = SaveTreeSafelyAsync();
            SetStatus($"グループ「{group.Name}」を削除しました");
            return;
        }
        var groupId = ParseDbGroupId(group.Key);
        if (groupId is null) return;
        try
        {
            await _api.DeleteLayerGroupAsync(groupId.Value, CancellationToken.None);
            await ReloadLayersAsync();
            SetStatus($"グループ「{group.Name}」を削除しました");
        }
        catch (Exception ex)
        {
            SetStatus($"group delete failed: {ex.Message}");
        }
    }

    // 名前入力用の最小ダイアログ (OK で trim 後の非空文字列、キャンセル/空は null)
    private string? PromptForName(string title, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 88),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };
        var box = new TextBox { Left = 12, Top = 12, Width = 296, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 152, Top = 48, Width = 75 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 233, Top = 48, Width = 75 };
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        if (form.ShowDialog(this) != DialogResult.OK) return null;
        var name = box.Text.Trim();
        return name.Length == 0 ? null : name;
    }

    // ====== LG303: 永続化 (best-effort、失敗は status bar のみ) ======

    private async Task SaveTreeSafelyAsync()
    {
        try
        {
            await _controller.SaveTreeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus($"save layer tree failed: {ex.Message}");
        }
    }

    private async Task SaveFlagsSafelyAsync()
    {
        try
        {
            await _controller.SaveFlagsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus($"save layer flags failed: {ex.Message}");
        }
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
        // Controller が再 reload した結果をツリーに反映
        ApplyReloadResult(new ReloadResult(_controller.Layers, _controller.Layers.Count > 0 ? 0 : -1));
        ApplyGuestRestriction();
        SetStatus("Re-authenticated");
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
