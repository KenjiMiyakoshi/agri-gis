using System.Text.Json;

namespace AgriGis.Desktop.Core.LayerTree;

// LG201/LG202 (Phase LG WLG2): UI 非依存のレイヤツリーモデル。
// - ノード構造 (グループ多階層 + レイヤ) の保持と移動/可視/フラグ操作
// - EnumerateVisibleLayerIdsDfs() = z-order (先頭=前面)。既存 layer_order_change envelope にそのまま渡せる
// - layer_tree_v1 preference との相互変換 (Visible / Edit / Snap は含めない)
// - デフォルトツリー (DB) と user preference のマージ規則 (Merge)
// - 旧 layer_order_v1 からの移行 (MigrateFromFlatOrder)
//
// 設計判断: ルートは「外部に公開しない仮想ルートノード (IsRoot=true)」で表現する。
// 公開 API では parentKey=null がルートを意味し、ルート自体の Key は使われない。
// これにより「親内の挿入/退避/並べ替え」のロジックがルートと通常グループで共通化できる。
public sealed class LayerTreeModel
{
    /// <summary>user_preference のキー (ツリー構造 snapshot)。</summary>
    public const string PreferenceKey = "layer_tree_v1";

    private readonly TreeGroupNode _root = new(key: "", name: "", expanded: true, isRoot: true);

    /// <summary>ルート直下のノード (グループ + レイヤ混在、Order 順)。</summary>
    public IReadOnlyList<LayerTreeNode> RootNodes => _root.Children;

    // ---------------------------------------------------------------
    // 構築
    // ---------------------------------------------------------------

    /// <summary>
    /// グループ定義 + レイヤ配置からツリーを構築する (デフォルトツリー組み立て用)。
    /// 寛容動作: 重複 key / 重複 layerId は先勝ち、未知の parent はルート直下、
    /// parent の循環はルート直下に退避して握り潰す (落とさない)。
    /// </summary>
    public static LayerTreeModel Build(
        IEnumerable<TreeGroupDefinition> groups,
        IEnumerable<TreeLayerPlacement> layers)
    {
        var model = new LayerTreeModel();

        var defs = new List<TreeGroupDefinition>();
        var defByKey = new Dictionary<string, TreeGroupDefinition>(StringComparer.Ordinal);
        foreach (var d in groups)
        {
            if (string.IsNullOrEmpty(d.Key) || defByKey.ContainsKey(d.Key)) continue;
            defByKey[d.Key] = d;
            defs.Add(d);
        }

        var nodeByKey = new Dictionary<string, TreeGroupNode>(StringComparer.Ordinal);
        foreach (var d in defs)
        {
            nodeByKey[d.Key] = new TreeGroupNode(d.Key, d.Name ?? string.Empty, d.Expanded);
        }

        foreach (var d in defs)
        {
            var node = nodeByKey[d.Key];
            var parent = ResolveBuildParent(d, defByKey, nodeByKey, model._root);
            node.Order = d.Order;
            node.ParentNode = parent;
            parent.ChildList.Add(node);
        }

        var placedLayerIds = new HashSet<int>();
        foreach (var l in layers)
        {
            if (!placedLayerIds.Add(l.LayerId)) continue;
            var parent = l.ParentKey is not null && nodeByKey.TryGetValue(l.ParentKey, out var p)
                ? p
                : model._root;
            var node = new TreeLayerNode(l.LayerId) { Order = l.Order, ParentNode = parent };
            parent.ChildList.Add(node);
        }

        model.SortAllByOrder();
        return model;
    }

    private static TreeGroupNode ResolveBuildParent(
        TreeGroupDefinition def,
        Dictionary<string, TreeGroupDefinition> defByKey,
        Dictionary<string, TreeGroupNode> nodeByKey,
        TreeGroupNode root)
    {
        // 祖先チェーンを辿って循環を検出。循環ならルート直下に退避 (寛容)。
        // 未知 key に当たったらチェーン終端とみなす (中間が消えていても直近親には付ける)。
        var seen = new HashSet<string>(StringComparer.Ordinal) { def.Key };
        var pk = def.ParentKey;
        while (pk is not null)
        {
            if (!seen.Add(pk)) return root; // 循環
            if (!defByKey.TryGetValue(pk, out var pd)) break;
            pk = pd.ParentKey;
        }

        return def.ParentKey is not null && nodeByKey.TryGetValue(def.ParentKey, out var pn)
            ? pn
            : root;
    }

    // ---------------------------------------------------------------
    // 検索 / 列挙 (read アクセサ)
    // ---------------------------------------------------------------

    /// <summary>key のグループを返す。無ければ null。仮想ルートは対象外。</summary>
    public TreeGroupNode? FindGroup(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var g in EnumerateGroups())
        {
            if (string.Equals(g.Key, key, StringComparison.Ordinal)) return g;
        }
        return null;
    }

    /// <summary>layerId のレイヤノードを返す。無ければ null。</summary>
    public TreeLayerNode? FindLayer(int layerId)
    {
        foreach (var l in EnumerateLayerNodes())
        {
            if (l.LayerId == layerId) return l;
        }
        return null;
    }

    /// <summary>全グループを DFS (上から) で列挙。仮想ルートは含まない。</summary>
    public IReadOnlyList<TreeGroupNode> EnumerateGroups()
        => Dfs(_root).OfType<TreeGroupNode>().ToList();

    /// <summary>全レイヤノードを DFS (上から) で列挙。</summary>
    public IReadOnlyList<TreeLayerNode> EnumerateLayerNodes()
        => Dfs(_root).OfType<TreeLayerNode>().ToList();

    /// <summary>全レイヤ id を DFS (上から) で列挙。</summary>
    public IReadOnlyList<int> EnumerateAllLayerIds()
        => Dfs(_root).OfType<TreeLayerNode>().Select(l => l.LayerId).ToList();

    /// <summary>
    /// Visible=true のレイヤ id を上から DFS で列挙。これが z-order (先頭=前面) であり、
    /// 既存の layer_order_change envelope にそのまま渡す。
    /// </summary>
    public IReadOnlyList<int> EnumerateVisibleLayerIdsDfs()
        => Dfs(_root).OfType<TreeLayerNode>().Where(l => l.Visible).Select(l => l.LayerId).ToList();

    private static IEnumerable<LayerTreeNode> Dfs(TreeGroupNode group)
    {
        foreach (var child in group.ChildList)
        {
            yield return child;
            if (child is TreeGroupNode g)
            {
                foreach (var d in Dfs(g)) yield return d;
            }
        }
    }

    // ---------------------------------------------------------------
    // 構造操作
    // ---------------------------------------------------------------

    /// <summary>レイヤを parentKey (null=ルート) の order 位置へ移動する。</summary>
    /// <remarks>
    /// 単一移動は MoveLayers へ委譲する (移動ロジック重複排除)。ただし MoveLayers は
    /// 「未知 id は無視」だが、単一 MoveLayer は従来通り未知 id で KeyNotFoundException を
    /// 投げる契約 (UI の単一 D&D は存在保証を呼び出し側に要求してきた) を維持するため、
    /// 存在検証だけ先に行う。
    /// </remarks>
    public void MoveLayer(int layerId, string? parentKey, int order)
    {
        if (FindLayer(layerId) is null)
            throw new KeyNotFoundException($"layer not found: {layerId}");
        MoveLayers(new[] { layerId }, parentKey, order);
    }

    /// <summary>
    /// LGP201 (Phase LG' WLGP2): 複数レイヤをアトミックに parentKey (null=ルート) 配下へ移動する。
    /// 元位置から全レイヤを除去 → parent 配下に startOrder から入力順で連番挿入する。
    /// - 入力 layerIds の順序を保持して挿入する。
    /// - 存在しない layerId は無視し、残りを処理する (全件 unknown でも例外にしない)。
    /// - 重複 layerId は先勝ち (2 回目以降は無視)。
    /// - parentKey が存在しないグループキーの場合は既存 MoveLayer 同様 KeyNotFoundException
    ///   (ResolveParent の挙動に倣う。寛容にしたい Build/Merge とは別経路の明示操作のため厳格)。
    ///
    /// index ずれ処理: parent の現在の子数に対し startOrder を 0..count に正規化 (InsertAt の Clamp)。
    /// 同一 parent 内移動でも、除去で後続 index が詰まった後の clamp 済み位置へ順次挿入するため、
    /// 入力順がそのまま startOrder 起点の連続スロットに反映される (Order は常に 0..n-1 に正規化)。
    /// </summary>
    public void MoveLayers(IReadOnlyList<int> layerIds, string? parentKey, int startOrder)
    {
        var parent = ResolveParent(parentKey);

        var seen = new HashSet<int>();
        var targets = new List<TreeLayerNode>();
        foreach (var id in layerIds)
        {
            if (!seen.Add(id)) continue; // 重複は先勝ち
            if (FindLayer(id) is { } layer) targets.Add(layer); // 未知 id は無視
        }
        if (targets.Count == 0) return;

        foreach (var layer in targets) Detach(layer);

        // 除去後の parent 子数に対し startOrder を正規化し、そこから入力順で連続挿入する。
        var insertAt = Math.Clamp(startOrder, 0, parent.ChildList.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            InsertAt(parent, targets[i], insertAt + i);
        }
    }

    /// <summary>
    /// グループを parentKey (null=ルート) の order 位置へ移動する。
    /// 自分自身 / 自分の子孫への移動は InvalidOperationException。
    /// </summary>
    public void MoveGroup(string key, string? parentKey, int order)
    {
        var group = FindGroup(key)
            ?? throw new KeyNotFoundException($"group not found: {key}");
        var parent = ResolveParent(parentKey);
        if (ReferenceEquals(parent, group) || IsDescendantOf(parent, group))
        {
            throw new InvalidOperationException(
                $"cannot move group '{key}' into itself or its descendant");
        }
        Detach(group);
        InsertAt(parent, group, order);
    }

    /// <summary>
    /// ユーザ独自グループを parentKey (null=ルート) の末尾に作成し、生成した key を返す。
    /// key は "usr:" + ランダム 8 hex (Guid ベース)。
    /// </summary>
    public string CreateUserGroup(string name, string? parentKey)
    {
        var parent = ResolveParent(parentKey);
        string key;
        do
        {
            key = "usr:" + Guid.NewGuid().ToString("N")[..8];
        } while (FindGroup(key) is not null);

        var node = new TreeGroupNode(key, name, expanded: true);
        InsertAt(parent, node, parent.ChildList.Count);
        return key;
    }

    /// <summary>
    /// グループを削除する。中のレイヤと子グループは削除グループの親
    /// (トップレベルなら仮想ルート) の、元グループがあった位置へ順序維持で退避する。
    /// </summary>
    public void RemoveGroup(string key)
    {
        var group = FindGroup(key)
            ?? throw new KeyNotFoundException($"group not found: {key}");
        var parent = group.ParentNode ?? _root;
        var insertIndex = group.Order;
        Detach(group);

        var children = group.ChildList.ToList();
        group.ChildList.Clear();
        for (var i = 0; i < children.Count; i++)
        {
            children[i].ParentNode = null;
            InsertAt(parent, children[i], insertIndex + i);
        }
    }

    // ---------------------------------------------------------------
    // 可視 / フラグ操作 (セッション状態。未知 id/key は寛容に無視)
    // ---------------------------------------------------------------

    /// <summary>レイヤの表示 ON/OFF。未知の layerId は無視 (stale id 耐性)。</summary>
    public void SetLayerVisible(int layerId, bool visible)
    {
        var layer = FindLayer(layerId);
        if (layer is not null) layer.Visible = visible;
    }

    /// <summary>レイヤの編集/スナップフラグ。null の引数は変更しない。未知の layerId は無視。</summary>
    public void SetLayerFlags(int layerId, bool? edit = null, bool? snap = null)
    {
        var layer = FindLayer(layerId);
        if (layer is null) return;
        if (edit.HasValue) layer.EditEnabled = edit.Value;
        if (snap.HasValue) layer.SnapEnabled = snap.Value;
    }

    /// <summary>グループの展開状態。未知の key は無視。</summary>
    public void SetGroupExpanded(string key, bool expanded)
    {
        var group = FindGroup(key);
        if (group is not null) group.Expanded = expanded;
    }

    /// <summary>グループ配下の全子孫レイヤの Visible を一括設定する。</summary>
    public void SetGroupVisible(string key, bool visible)
    {
        var group = FindGroup(key)
            ?? throw new KeyNotFoundException($"group not found: {key}");
        foreach (var layer in Dfs(group).OfType<TreeLayerNode>())
        {
            layer.Visible = visible;
        }
    }

    /// <summary>子孫レイヤの Visible 集計による 3 値。子孫レイヤ 0 個なら Unchecked。</summary>
    public GroupCheckState GetGroupCheckState(string key)
    {
        var group = FindGroup(key)
            ?? throw new KeyNotFoundException($"group not found: {key}");
        var total = 0;
        var visible = 0;
        foreach (var layer in Dfs(group).OfType<TreeLayerNode>())
        {
            total++;
            if (layer.Visible) visible++;
        }
        if (total == 0 || visible == 0) return GroupCheckState.Unchecked;
        return visible == total ? GroupCheckState.Checked : GroupCheckState.Mixed;
    }

    // ---------------------------------------------------------------
    // シリアライズ (layer_tree_v1)
    // ---------------------------------------------------------------

    /// <summary>
    /// layer_tree_v1 形式の JSON を返す。Visible / EditEnabled / SnapEnabled は含めない
    /// (visible はセッション状態、flags は layer_flags_v1 に分離)。
    /// </summary>
    public string ToPreferenceJson()
    {
        var groups = EnumerateGroups()
            .Select(g => new
            {
                key = g.Key,
                name = g.Name,
                parent = g.ParentKey,
                order = g.Order,
                expanded = g.Expanded,
            });
        var layers = EnumerateLayerNodes()
            .Select(l => new
            {
                layerId = l.LayerId,
                parent = l.ParentKey,
                order = l.Order,
            });
        return JsonSerializer.Serialize(new { groups, layers });
    }

    /// <summary>
    /// layer_tree_v1 JSON からツリーを復元する。不正 JSON は空ツリー、
    /// 欠損フィールドはデフォルト値 (parent=null / order=0 / expanded=true)、
    /// key/layerId 欠損エントリと未知 parent / 循環 parent は寛容に処理する (落ちない)。
    /// </summary>
    public static LayerTreeModel FromPreferenceJson(string? json)
    {
        TryParsePreference(json, out var model);
        return model;
    }

    /// <summary>寛容パース。JSON として不正な場合のみ false (model は空ツリー)。</summary>
    internal static bool TryParsePreference(string? json, out LayerTreeModel model)
    {
        model = new LayerTreeModel();
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var groupDefs = new List<TreeGroupDefinition>();
            if (root.TryGetProperty("groups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object) continue;
                    var key = TryGetString(g, "key");
                    if (string.IsNullOrEmpty(key)) continue;
                    groupDefs.Add(new TreeGroupDefinition(
                        key,
                        TryGetString(g, "name") ?? string.Empty,
                        TryGetString(g, "parent"),
                        TryGetInt(g, "order") ?? 0,
                        TryGetBool(g, "expanded") ?? true));
                }
            }

            var layerDefs = new List<TreeLayerPlacement>();
            if (root.TryGetProperty("layers", out var layersEl) && layersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in layersEl.EnumerateArray())
                {
                    if (l.ValueKind != JsonValueKind.Object) continue;
                    var layerId = TryGetInt(l, "layerId");
                    if (layerId is null) continue;
                    layerDefs.Add(new TreeLayerPlacement(
                        layerId.Value,
                        TryGetString(l, "parent"),
                        TryGetInt(l, "order") ?? 0));
                }
            }

            model = Build(groupDefs, layerDefs);
            return true;
        }
        catch (JsonException)
        {
            model = new LayerTreeModel();
            return false;
        }
    }

    // ---------------------------------------------------------------
    // LG202: マージ規則
    // ---------------------------------------------------------------

    /// <summary>
    /// デフォルトツリー (DB 由来) と user preference (layer_tree_v1 JSON) をマージする。
    /// - preference 無し / 不正 JSON → defaultTree (availableLayerIds でフィルタ) をそのまま採用
    /// - preference 有り → preference のツリー構造を採用した上で:
    ///   - "db:N" グループ名は defaultTree 側を優先 (admin rename 追従)
    ///   - defaultTree に無い "db:N" グループは消滅扱い → グループ自体は捨て、中身はルートへ
    ///   - "usr:" グループはそのまま
    ///   - preference に無い availableLayerIds のレイヤ → defaultTree の位置へ
    ///     (所属グループがマージ結果に無ければ defaultTree から祖先チェーンごと復元。
    ///      defaultTree にも無いレイヤはルート末尾)
    ///   - preference にあるが availableLayerIds に無いレイヤ → 無視
    /// Visible / フラグ: defaultTree 由来で配置されたレイヤは defaultTree の値を引き継ぎ、
    /// preference 由来は false 初期化 (セッション状態は呼び出し側が別途復元する)。
    /// </summary>
    public static LayerTreeModel Merge(
        LayerTreeModel defaultTree,
        string? preferenceJson,
        IReadOnlyCollection<int> availableLayerIds)
    {
        var available = new HashSet<int>(availableLayerIds);

        if (!TryParsePreference(preferenceJson, out var pref))
        {
            return CloneFiltered(defaultTree, availableLayerIds, available);
        }

        var result = new LayerTreeModel();
        var defaultGroupsByKey = defaultTree.EnumerateGroups()
            .ToDictionary(g => g.Key, StringComparer.Ordinal);

        CopyPreferenceSubtree(pref._root, result._root, result, defaultGroupsByKey, available);

        // preference に無い available レイヤを defaultTree の位置へ (defaultTree の DFS 順)
        var placed = new HashSet<int>(result.EnumerateAllLayerIds());
        foreach (var defLayer in defaultTree.EnumerateLayerNodes())
        {
            if (!available.Contains(defLayer.LayerId) || !placed.Add(defLayer.LayerId)) continue;
            var parent = result.EnsureGroupChain(defLayer.ParentNode);
            var node = CopyLayerNode(defLayer);
            InsertAt(parent, node, parent.ChildList.Count);
        }

        // defaultTree にも preference にも無い available レイヤ → ルート末尾 (与えられた順)
        foreach (var id in availableLayerIds)
        {
            if (!placed.Add(id)) continue;
            InsertAt(result._root, new TreeLayerNode(id), result._root.ChildList.Count);
        }

        return result;
    }

    /// <summary>
    /// 旧 layer_order_v1 (可視レイヤ id のフラット順序リスト) からの移行 (layer_tree_v1 不在時 1 回限り)。
    ///
    /// 仕様 (安全優先、厳密な z-order 再現はしない):
    /// - ツリー構造 (グループ所属) は defaultTree をそのまま採用する
    /// - flatOrder に含まれるレイヤを Visible=true、含まれないレイヤを Visible=false とする
    /// - 順序は「同一親グループ内のレイヤ同士の相対順序」にのみ flatOrder を反映する。
    ///   グループ跨ぎの順序はツリー構造 (DFS) 優先で、flatOrder と食い違っても構造を崩さない
    /// - flatOrder 中の未知レイヤ id は無視
    /// </summary>
    public static LayerTreeModel MigrateFromFlatOrder(
        LayerTreeModel defaultTree,
        IReadOnlyList<int> flatOrder)
    {
        var result = CloneFiltered(defaultTree, Array.Empty<int>(), filter: null);

        var orderIndex = new Dictionary<int, int>();
        for (var i = 0; i < flatOrder.Count; i++)
        {
            if (!orderIndex.ContainsKey(flatOrder[i])) orderIndex[flatOrder[i]] = i;
        }

        foreach (var layer in result.EnumerateLayerNodes())
        {
            layer.Visible = orderIndex.ContainsKey(layer.LayerId);
        }

        // 各親グループ内で、flatOrder に含まれるレイヤ同士を flatOrder 順に並べ替える。
        // 対象レイヤが占めていたスロットだけを入れ替え、グループや対象外レイヤの位置は動かさない。
        foreach (var parent in result.AllGroupsIncludingRoot())
        {
            var slots = new List<int>();
            for (var i = 0; i < parent.ChildList.Count; i++)
            {
                if (parent.ChildList[i] is TreeLayerNode l && orderIndex.ContainsKey(l.LayerId))
                {
                    slots.Add(i);
                }
            }
            if (slots.Count < 2) continue;

            var sorted = slots
                .Select(i => (TreeLayerNode)parent.ChildList[i])
                .OrderBy(l => orderIndex[l.LayerId])
                .ToList();
            for (var i = 0; i < slots.Count; i++)
            {
                parent.ChildList[slots[i]] = sorted[i];
            }
            Renumber(parent);
        }

        return result;
    }

    // preference のサブツリーを result へコピーする (Merge 専用)。
    // 消滅した db: グループは自身を捨てて中身をルートへ流す。
    private static void CopyPreferenceSubtree(
        TreeGroupNode source,
        TreeGroupNode destination,
        LayerTreeModel result,
        Dictionary<string, TreeGroupNode> defaultGroupsByKey,
        HashSet<int> available)
    {
        foreach (var child in source.ChildList)
        {
            switch (child)
            {
                case TreeLayerNode l:
                    if (!available.Contains(l.LayerId)) continue; // 消滅レイヤは無視
                    if (result.FindLayer(l.LayerId) is not null) continue; // 重複防御 (先勝ち)
                    InsertAt(destination, new TreeLayerNode(l.LayerId), destination.ChildList.Count);
                    break;

                case TreeGroupNode g:
                    var isDb = g.Key.StartsWith("db:", StringComparison.Ordinal);
                    if (isDb && !defaultGroupsByKey.ContainsKey(g.Key))
                    {
                        // 消滅 db: グループ → 中身 (レイヤ/子グループ) はルートへ退避
                        CopyPreferenceSubtree(g, result._root, result, defaultGroupsByKey, available);
                        continue;
                    }
                    if (result.FindGroup(g.Key) is TreeGroupNode existing)
                    {
                        // 重複 key 防御: 既存ノードへ中身だけ合流
                        CopyPreferenceSubtree(g, existing, result, defaultGroupsByKey, available);
                        continue;
                    }
                    var name = isDb ? defaultGroupsByKey[g.Key].Name : g.Name;
                    var node = new TreeGroupNode(g.Key, name, g.Expanded);
                    InsertAt(destination, node, destination.ChildList.Count);
                    CopyPreferenceSubtree(g, node, result, defaultGroupsByKey, available);
                    break;
            }
        }
    }

    // defaultTree 上の親グループチェーンを result 内に確保する (無ければ復元)。
    // 例: preference 保存後に admin が新グループ + 新レイヤを追加したケースで、
    // 新レイヤを「defaultTree の位置」へ置くために必要。
    private TreeGroupNode EnsureGroupChain(TreeGroupNode? defaultParent)
    {
        if (defaultParent is null || defaultParent.IsRoot) return _root;
        if (FindGroup(defaultParent.Key) is TreeGroupNode existing) return existing;
        var host = EnsureGroupChain(defaultParent.ParentNode);
        var node = new TreeGroupNode(defaultParent.Key, defaultParent.Name, defaultParent.Expanded);
        InsertAt(host, node, host.ChildList.Count);
        return node;
    }

    // defaultTree の deep copy。filter 非 null ならレイヤをフィルタし、
    // extraLayerIds (ツリーに無い available レイヤ) をルート末尾へ追加する。
    private static LayerTreeModel CloneFiltered(
        LayerTreeModel source,
        IEnumerable<int> extraLayerIds,
        HashSet<int>? filter)
    {
        var result = new LayerTreeModel();
        CloneChildren(source._root, result._root);

        var placed = new HashSet<int>(result.EnumerateAllLayerIds());
        foreach (var id in extraLayerIds)
        {
            if (!placed.Add(id)) continue;
            InsertAt(result._root, new TreeLayerNode(id), result._root.ChildList.Count);
        }
        return result;

        void CloneChildren(TreeGroupNode src, TreeGroupNode dst)
        {
            foreach (var child in src.ChildList)
            {
                switch (child)
                {
                    case TreeLayerNode l:
                        if (filter is not null && !filter.Contains(l.LayerId)) continue;
                        InsertAt(dst, CopyLayerNode(l), dst.ChildList.Count);
                        break;
                    case TreeGroupNode g:
                        var ng = new TreeGroupNode(g.Key, g.Name, g.Expanded);
                        InsertAt(dst, ng, dst.ChildList.Count);
                        CloneChildren(g, ng);
                        break;
                }
            }
        }
    }

    private static TreeLayerNode CopyLayerNode(TreeLayerNode source)
        => new(source.LayerId)
        {
            Visible = source.Visible,
            EditEnabled = source.EditEnabled,
            SnapEnabled = source.SnapEnabled,
        };

    // ---------------------------------------------------------------
    // 内部ヘルパ
    // ---------------------------------------------------------------

    private TreeGroupNode ResolveParent(string? parentKey)
    {
        if (parentKey is null) return _root;
        return FindGroup(parentKey)
            ?? throw new KeyNotFoundException($"group not found: {parentKey}");
    }

    /// <summary>node が ancestor の子孫 (真の子孫) かどうか。</summary>
    private static bool IsDescendantOf(LayerTreeNode node, TreeGroupNode ancestor)
    {
        for (var p = node.ParentNode; p is not null; p = p.ParentNode)
        {
            if (ReferenceEquals(p, ancestor)) return true;
        }
        return false;
    }

    private static void Detach(LayerTreeNode node)
    {
        var parent = node.ParentNode;
        if (parent is null) return;
        parent.ChildList.Remove(node);
        node.ParentNode = null;
        Renumber(parent);
    }

    private static void InsertAt(TreeGroupNode parent, LayerTreeNode child, int order)
    {
        var index = Math.Clamp(order, 0, parent.ChildList.Count);
        parent.ChildList.Insert(index, child);
        child.ParentNode = parent;
        Renumber(parent);
    }

    private static void Renumber(TreeGroupNode parent)
    {
        for (var i = 0; i < parent.ChildList.Count; i++)
        {
            parent.ChildList[i].Order = i;
        }
    }

    private IEnumerable<TreeGroupNode> AllGroupsIncludingRoot()
    {
        yield return _root;
        foreach (var g in Dfs(_root).OfType<TreeGroupNode>()) yield return g;
    }

    // Build 直後に各親の子リストを要求 Order で安定ソートし、Order を 0..n-1 に正規化する。
    private void SortAllByOrder()
    {
        foreach (var g in AllGroupsIncludingRoot())
        {
            var sorted = g.ChildList.OrderBy(c => c.Order).ToList();
            g.ChildList.Clear();
            g.ChildList.AddRange(sorted);
            Renumber(g);
        }
    }

    private static string? TryGetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static int? TryGetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)
            ? n
            : null;

    private static bool? TryGetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : null;
}
