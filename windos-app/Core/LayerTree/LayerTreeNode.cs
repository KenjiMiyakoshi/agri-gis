namespace AgriGis.Desktop.Core.LayerTree;

// LG201 (Phase LG WLG2): レイヤツリーのノード定義。
// UI 非依存 (System.Windows.Forms 不参照、CoreLayerIsolationTest 対象)。

/// <summary>
/// ツリーノード基底。Order は同一親内の表示位置で、モデル操作後は常に
/// 親 Children のインデックスと一致するよう正規化される。
/// </summary>
public abstract class LayerTreeNode
{
    /// <summary>同一親内の表示順 (0 始まり、Children のインデックスと一致)。</summary>
    public int Order { get; internal set; }

    /// <summary>親グループノード。仮想ルート直下の場合はルートノード自身を指す。</summary>
    internal TreeGroupNode? ParentNode { get; set; }

    /// <summary>親グループの key。ルート直下 (仮想ルートの子) は null。</summary>
    public string? ParentKey => ParentNode is { IsRoot: false } p ? p.Key : null;
}

/// <summary>
/// グループノード。Key は "db:N" (admin デフォルト、N は layer_group.group_id) /
/// "usr:xxxxxxxx" (ユーザ独自、ランダム 8 hex)。
/// </summary>
public sealed class TreeGroupNode : LayerTreeNode
{
    internal TreeGroupNode(string key, string name, bool expanded, bool isRoot = false)
    {
        Key = key;
        Name = name;
        Expanded = expanded;
        IsRoot = isRoot;
    }

    public string Key { get; }
    public string Name { get; internal set; }
    public bool Expanded { get; internal set; }

    /// <summary>仮想ルートかどうか。ルートは API 上は parentKey=null として扱い、外部公開しない。</summary>
    internal bool IsRoot { get; }

    internal List<LayerTreeNode> ChildList { get; } = new();

    /// <summary>子ノード (グループ + レイヤ混在、Order 順)。</summary>
    public IReadOnlyList<LayerTreeNode> Children => ChildList;
}

/// <summary>
/// レイヤノード。Visible はセッション状態、EditEnabled / SnapEnabled は
/// layer_flags_v1 preference 由来のフラグで、いずれも layer_tree_v1 JSON には含めない。
/// </summary>
public sealed class TreeLayerNode : LayerTreeNode
{
    internal TreeLayerNode(int layerId)
    {
        LayerId = layerId;
    }

    public int LayerId { get; }
    public bool Visible { get; internal set; }
    public bool EditEnabled { get; internal set; }
    public bool SnapEnabled { get; internal set; }
}

/// <summary>グループの表示チェック 3 値 (子孫レイヤの Visible 集計)。</summary>
public enum GroupCheckState
{
    Checked,
    Unchecked,
    Mixed,
}

/// <summary>
/// デフォルトツリー (DB 由来) のグループ定義。WLG3 controller が
/// LayerGroupDto (key="db:" + groupId) から組み立てる想定。
/// </summary>
public sealed record TreeGroupDefinition(
    string Key,
    string Name,
    string? ParentKey,
    int Order,
    bool Expanded = true);

/// <summary>デフォルトツリーのレイヤ配置。ParentKey=null はルート直下。</summary>
public sealed record TreeLayerPlacement(int LayerId, string? ParentKey, int Order);
