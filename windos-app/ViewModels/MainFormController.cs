using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core.LayerTree;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.ViewModels;

// H5-101 (WH5-1): MainForm から layers 管理 + Unauthorized 復旧経路を切り出した Controller。
// LG303 (Phase LG WLG3): フラットな visible set + order list を LayerTreeModel (WLG2) に置換。
//
// 設計:
// - UI 非依存 (TreeView / Form を直接操作しない)
// - ReloadAsync で「デフォルトツリー (GET /api/layer-groups + LayerDto.GroupId/SortOrder)
//   × user preference (layer_tree_v1)」をマージし、layer_flags_v1 を適用する
// - layer_tree_v1 不在時は旧 layer_order_v1 を順序 seed に 1 回限り移行 (MigrateFromFlatOrder)
// - OrderedLayerIds = model.EnumerateVisibleLayerIdsDfs() (z-order、既存呼び出し互換)
// - 永続化は SaveTreeAsync (layer_tree_v1) / SaveFlagsAsync (layer_flags_v1)。
//   呼び出しタイミング (best-effort) は MainForm 側の責務
public sealed class MainFormController
{
    private readonly IApiClient _api;
    private readonly ISessionStore _session;
    private readonly AsOfState _asOf;
    private IReadOnlyList<LayerDto> _layers = Array.Empty<LayerDto>();
    private LayerTreeModel _tree = new();

    /// <summary>旧フラット順序キー (deprecated)。layer_tree_v1 不在時の移行 seed にのみ使用。</summary>
    public const string LayerOrderPreferenceKey = "layer_order_v1";

    /// <summary>編集/スナップフラグの永続化キー ({"5": {"edit": true, "snap": false}} 形式)。</summary>
    public const string FlagsPreferenceKey = "layer_flags_v1";

    public MainFormController(IApiClient api, ISessionStore session, AsOfState asOf)
    {
        _api = api;
        _session = session;
        _asOf = asOf;
    }

    /// <summary>現在保持している layer 一覧 (read only)。</summary>
    public IReadOnlyList<LayerDto> Layers => _layers;

    /// <summary>現在の session (read only)。</summary>
    public Session? CurrentSession => _session.Current;

    /// <summary>LG303: 現在のレイヤツリー (read アクセス用。構造変更は本 Controller 経由で行う)。</summary>
    public LayerTreeModel Tree => _tree;

    /// <summary>F302 互換: 現在 ON になっている layer_id 集合 (snapshot)。</summary>
    public IReadOnlySet<int> VisibleLayerIds => new HashSet<int>(_tree.EnumerateVisibleLayerIdsDfs());

    /// <summary>
    /// F'303 互換: 現在 ON の layer_id を z-order 順 (上位 = 前面) で取得。
    /// LG303 以降は「ツリーの可視レイヤを上から DFS で列挙した順」(= layer_order_change にそのまま渡す)。
    /// </summary>
    public IReadOnlyList<int> OrderedLayerIds => _tree.EnumerateVisibleLayerIdsDfs();

    // ---------------------------------------------------------------
    // ツリー操作 (model 委譲)
    // ---------------------------------------------------------------

    /// <summary>layer 表示状態の切替。未知の layerId は無視。</summary>
    public void SetLayerVisible(int layerId, bool visible)
        => _tree.SetLayerVisible(layerId, visible);

    /// <summary>編集/スナップフラグ (null の引数は変更しない)。未知の layerId は無視。</summary>
    public void SetLayerFlags(int layerId, bool? edit = null, bool? snap = null)
        => _tree.SetLayerFlags(layerId, edit, snap);

    /// <summary>グループ配下の全子孫レイヤの Visible を一括設定する。</summary>
    public void SetGroupVisible(string key, bool visible)
        => _tree.SetGroupVisible(key, visible);

    /// <summary>グループの 3 値チェック状態 (子孫レイヤの Visible 集計)。</summary>
    public GroupCheckState GetGroupCheckState(string key)
        => _tree.GetGroupCheckState(key);

    /// <summary>グループの展開状態を記録する (layer_tree_v1 に保存される)。</summary>
    public void SetGroupExpanded(string key, bool expanded)
        => _tree.SetGroupExpanded(key, expanded);

    /// <summary>レイヤを parentKey (null=ルート) の order 位置へ移動する。</summary>
    public void MoveLayer(int layerId, string? parentKey, int order)
        => _tree.MoveLayer(layerId, parentKey, order);

    /// <summary>グループ移動。自分自身/子孫への移動は InvalidOperationException。</summary>
    public void MoveGroup(string key, string? parentKey, int order)
        => _tree.MoveGroup(key, parentKey, order);

    /// <summary>ユーザ独自グループ (usr:xxxx) を作成し key を返す。pref のみ、DB には作らない。</summary>
    public string CreateUserGroup(string name, string? parentKey)
        => _tree.CreateUserGroup(name, parentKey);

    /// <summary>グループ削除 (中身は親へ退避)。usr: は pref のみ、db: は admin API 成功後の Reload で反映。</summary>
    public void RemoveGroup(string key)
        => _tree.RemoveGroup(key);

    /// <summary>ユーザ独自グループ (usr:) の改名。db: グループは DB 側が正なので対象外。</summary>
    public void RenameUserGroup(string key, string name)
    {
        if (!key.StartsWith("usr:", StringComparison.Ordinal)) return;
        var group = _tree.FindGroup(key);
        if (group is not null) group.Name = name;
    }

    /// <summary>F302: layer_id から layer 情報を取得 (見つからなければ null)。</summary>
    public LayerDto? GetLayerById(int layerId)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].LayerId == layerId) return _layers[i];
        }
        return null;
    }

    // ---------------------------------------------------------------
    // Reload (ツリー構築 + マージ + flags 適用)
    // ---------------------------------------------------------------

    /// <summary>
    /// API から layer 一覧 + グループ一覧を再取得し、ツリーを再構築する。
    /// - defaultTree = layer-groups (key "db:N") + LayerDto.GroupId/SortOrder
    /// - layer_tree_v1 があれば Merge、無ければ layer_order_v1 を seed に移行 (1 回限り)
    /// - 再 reload では現セッションの表示 ON 状態を引き継ぐ (削除された layer は落ちる)
    /// - 初回 (可視レイヤ 0 件) は先頭 layer を初期 ON (既存挙動維持)
    /// - layer_flags_v1 の編集/スナップフラグを適用
    /// </summary>
    public async Task<ReloadResult> ReloadAsync(int? prevSelectedLayerId, CancellationToken ct)
    {
        var prevVisible = _tree.EnumerateVisibleLayerIdsDfs();

        _layers = await _api.GetLayersAsync(_asOf.Current, ct);
        var groups = await _api.GetLayerGroupsAsync(ct);
        var defaultTree = BuildDefaultTree(groups, _layers);
        var availableIds = _layers.Select(l => l.LayerId).ToList();

        var treePref = await _api.GetUserPreferenceAsync(LayerTreeModel.PreferenceKey, ct);
        if (treePref is not null)
        {
            _tree = LayerTreeModel.Merge(defaultTree, treePref.Value.GetRawText(), availableIds);
        }
        else
        {
            // R4: 旧 layer_order_v1 からの移行 (layer_tree_v1 不在時 1 回限り)。
            // MigrateFromFlatOrder が flatOrder 中のレイヤを Visible=true にする。
            var flatOrder = await LoadFlatLayerOrderAsync(ct);
            _tree = flatOrder is { Count: > 0 }
                ? LayerTreeModel.MigrateFromFlatOrder(defaultTree, flatOrder)
                : LayerTreeModel.Merge(defaultTree, null, availableIds);
        }

        // セッション中の表示 ON 状態を引き継ぐ (削除済 layer は新ツリーに無いので自然に落ちる)
        foreach (var id in prevVisible)
        {
            _tree.SetLayerVisible(id, true);
        }

        // 初回 (可視 0 件 + 層がある) は先頭を ON — 既存挙動維持。
        // 移行で visible が立った場合はそちらを優先する。
        if (_layers.Count > 0 && _tree.EnumerateVisibleLayerIdsDfs().Count == 0)
        {
            _tree.SetLayerVisible(_layers[0].LayerId, true);
        }

        await ApplyFlagsPreferenceAsync(ct);

        var restoreIndex = ComputeRestoreIndex(_layers, prevSelectedLayerId);
        return new ReloadResult(_layers, restoreIndex);
    }

    /// <summary>LayerGroupDto + LayerDto.GroupId/SortOrder からデフォルトツリーを組み立てる。</summary>
    private static LayerTreeModel BuildDefaultTree(
        IReadOnlyList<LayerGroupDto> groups, IReadOnlyList<LayerDto> layers)
        => LayerTreeModel.Build(
            groups.Select(g => new TreeGroupDefinition(
                Key: $"db:{g.GroupId}",
                Name: g.GroupName,
                ParentKey: g.ParentGroupId is int p ? $"db:{p}" : null,
                Order: g.SortOrder)),
            layers.Select(l => new TreeLayerPlacement(
                LayerId: l.LayerId,
                ParentKey: l.GroupId is int gid ? $"db:{gid}" : null,
                Order: l.SortOrder)));

    // ---------------------------------------------------------------
    // 永続化 (layer_tree_v1 / layer_flags_v1 / 旧 layer_order_v1)
    // ---------------------------------------------------------------

    /// <summary>現在のツリー構造を layer_tree_v1 として永続化する。</summary>
    public Task SaveTreeAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(_tree.ToPreferenceJson());
        return _api.PutUserPreferenceAsync(LayerTreeModel.PreferenceKey,
            new UserPreferencePutDto(doc.RootElement.Clone()), ct);
    }

    /// <summary>編集/スナップフラグを layer_flags_v1 として永続化する (どちらかが ON のレイヤのみ)。</summary>
    public Task SaveFlagsAsync(CancellationToken ct)
    {
        var flags = new Dictionary<string, Dictionary<string, bool>>();
        foreach (var layer in _tree.EnumerateLayerNodes())
        {
            if (!layer.EditEnabled && !layer.SnapEnabled) continue;
            flags[layer.LayerId.ToString()] = new Dictionary<string, bool>
            {
                ["edit"] = layer.EditEnabled,
                ["snap"] = layer.SnapEnabled,
            };
        }
        var json = JsonSerializer.SerializeToElement(flags);
        return _api.PutUserPreferenceAsync(FlagsPreferenceKey, new UserPreferencePutDto(json), ct);
    }

    /// <summary>layer_flags_v1 を読み、現在のツリーに適用する (未知 id / 不正値は寛容に無視)。</summary>
    private async Task ApplyFlagsPreferenceAsync(CancellationToken ct)
    {
        var pref = await _api.GetUserPreferenceAsync(FlagsPreferenceKey, ct);
        if (pref is null || pref.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in pref.Value.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, out var layerId)) continue;
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            bool? edit = null;
            bool? snap = null;
            if (prop.Value.TryGetProperty("edit", out var e) &&
                e.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                edit = e.GetBoolean();
            }
            if (prop.Value.TryGetProperty("snap", out var s) &&
                s.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                snap = s.GetBoolean();
            }
            _tree.SetLayerFlags(layerId, edit, snap);
        }
    }

    /// <summary>旧 layer_order_v1 (可視レイヤ id のフラット順序) を読む。未設定/不正は null。</summary>
    private async Task<IReadOnlyList<int>?> LoadFlatLayerOrderAsync(CancellationToken ct)
    {
        var pref = await _api.GetUserPreferenceAsync(LayerOrderPreferenceKey, ct);
        if (pref is null) return null;
        if (pref.Value.ValueKind != JsonValueKind.Array) return null;
        var ids = new List<int>(pref.Value.GetArrayLength());
        foreach (var el in pref.Value.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var id))
                ids.Add(id);
        }
        return ids;
    }

    // ---------------------------------------------------------------
    // Unauthorized 復旧
    // ---------------------------------------------------------------

    /// <summary>
    /// 401 検知時のユーザー再認証フロー。
    /// loginProvider がログインフォームを返し、結果が OK なら再 reload して true を返す。
    /// キャンセル時は false (呼び出し側で Close 等の処理)。
    /// </summary>
    public async Task<bool> TryRecoverUnauthorizedAsync(
        Func<bool> showLoginAndReturnSuccess,
        CancellationToken ct)
    {
        _session.Clear();
        var success = showLoginAndReturnSuccess();
        if (!success) return false;
        // 再ログイン後の reload (LG303: ツリーも再構築する)
        await ReloadAsync(prevSelectedLayerId: null, ct);
        return true;
    }

    /// <summary>
    /// 純関数: layer 一覧と前回の selected layer_id から復元すべき index を計算する。
    /// 0 件 → -1、prev が見つからなければ 0 (先頭)、見つかればその index。
    /// </summary>
    public static int ComputeRestoreIndex(IReadOnlyList<LayerDto> layers, int? prevSelectedLayerId)
    {
        if (layers.Count == 0) return -1;
        if (prevSelectedLayerId is not { } pid) return 0;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].LayerId == pid) return i;
        }
        return 0;
    }
}

/// <summary>
/// ReloadAsync の結果。呼び出し側 (MainForm) がツリー再構築と
/// 初期 visibility/order 送出に使う。
/// </summary>
public sealed record ReloadResult(IReadOnlyList<LayerDto> Layers, int RestoreIndex);
