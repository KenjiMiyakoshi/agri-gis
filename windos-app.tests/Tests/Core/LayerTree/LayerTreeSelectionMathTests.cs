using AgriGis.Desktop.Core.LayerTree;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core.LayerTree;

// LGP301/LGP302 (Phase LG' WLGP3): 複数選択 D&D の純粋計算ロジック検証 (UI 非依存)。
// LayerTreeView (WinForms) はこの計算を TreeNode で呼ぶが、ロジック本体はジェネリックなので
// WinForms 参照なしで Shift 範囲計算 / detach 後 startOrder 算出を直接テストできる。
public sealed class LayerTreeSelectionMathTests
{
    // 参照同一性で扱われることを示すため、あえて等価な内容のノードを別インスタンスにする。
    private sealed class Node
    {
        public string Name { get; }
        public Node(string name) => Name = name;
        public override string ToString() => Name;
    }

    private static IReadOnlyList<Node> Nodes(params string[] names)
        => names.Select(n => new Node(n)).ToList();

    // ---------------- ComputeShiftRange ----------------

    [Fact]
    public void ShiftRange_AnchorBeforeClicked_ReturnsInclusiveForwardRange()
    {
        var v = Nodes("a", "b", "c", "d", "e");
        var range = LayerTreeSelectionMath.ComputeShiftRange(v, v[1], v[3]);
        Assert.Equal(new[] { v[1], v[2], v[3] }, range);
    }

    [Fact]
    public void ShiftRange_AnchorAfterClicked_ReturnsInclusiveRangeInVisibleOrder()
    {
        // anchor=d, clicked=b → 表示順 (b,c,d) を返す (選択方向に依らず可視順)
        var v = Nodes("a", "b", "c", "d", "e");
        var range = LayerTreeSelectionMath.ComputeShiftRange(v, v[3], v[1]);
        Assert.Equal(new[] { v[1], v[2], v[3] }, range);
    }

    [Fact]
    public void ShiftRange_AnchorEqualsClicked_ReturnsSingle()
    {
        var v = Nodes("a", "b", "c");
        var range = LayerTreeSelectionMath.ComputeShiftRange(v, v[1], v[1]);
        Assert.Equal(new[] { v[1] }, range);
    }

    [Fact]
    public void ShiftRange_NullAnchor_ReturnsClickedSingle()
    {
        var v = Nodes("a", "b", "c");
        var range = LayerTreeSelectionMath.ComputeShiftRange(v, null, v[2]);
        Assert.Equal(new[] { v[2] }, range);
    }

    [Fact]
    public void ShiftRange_AnchorNotInVisibleList_FallsBackToClickedSingle()
    {
        // アンカーが折りたたみ等で可視列から消えたケース → clicked 単独
        var v = Nodes("a", "b", "c");
        var hiddenAnchor = new Node("hidden");
        var range = LayerTreeSelectionMath.ComputeShiftRange(v, hiddenAnchor, v[0]);
        Assert.Equal(new[] { v[0] }, range);
    }

    [Fact]
    public void ShiftRange_ClickedNotInVisibleList_ReturnsEmpty()
    {
        var v = Nodes("a", "b");
        var orphan = new Node("orphan");
        Assert.Empty(LayerTreeSelectionMath.ComputeShiftRange(v, v[0], orphan));
    }

    // ---------------- ComputeMultiStartOrder ----------------

    [Fact]
    public void MultiStartOrder_NoSelectedBeforeBase_ReturnsBaseIndex()
    {
        // siblings: [g, x, y, z]、base=3 (z の上)、選択は無し → そのまま 3
        var s = Nodes("g", "x", "y", "z");
        var selected = new HashSet<Node>();
        Assert.Equal(3, LayerTreeSelectionMath.ComputeMultiStartOrder(s, 3, selected));
    }

    [Fact]
    public void MultiStartOrder_SelectedBeforeBase_ShiftsLeftByThatCount()
    {
        // siblings: [a, b, c, d]、base=3 (d の上に挿入)。a, b が移動対象 (base より前) なら
        // detach 後は base が 2 つ前にずれる → 1。
        var s = Nodes("a", "b", "c", "d");
        var selected = new HashSet<Node> { s[0], s[1] };
        Assert.Equal(1, LayerTreeSelectionMath.ComputeMultiStartOrder(s, 3, selected));
    }

    [Fact]
    public void MultiStartOrder_SelectedAtOrAfterBase_DoNotShift()
    {
        // base=2、選択は index 2,3 (base 以降) → 前詰めしない → 2
        var s = Nodes("a", "b", "c", "d");
        var selected = new HashSet<Node> { s[2], s[3] };
        Assert.Equal(2, LayerTreeSelectionMath.ComputeMultiStartOrder(s, 2, selected));
    }

    [Fact]
    public void MultiStartOrder_BaseAtEnd_AllSelectedBefore_ClampsToZero()
    {
        // 全選択を末尾へ drop: base=count、全員が base より前 → 0 起点に正規化
        var s = Nodes("a", "b", "c");
        var selected = new HashSet<Node> { s[0], s[1], s[2] };
        Assert.Equal(0, LayerTreeSelectionMath.ComputeMultiStartOrder(s, 3, selected));
    }

    [Fact]
    public void MultiStartOrder_BaseBeyondCount_IsClampedByLimit()
    {
        // 別 parent へ drop (siblings に選択は居ない) で base=count → そのまま count
        var s = Nodes("p", "q");
        var selected = new HashSet<Node> { new("foreign") };
        Assert.Equal(2, LayerTreeSelectionMath.ComputeMultiStartOrder(s, 2, selected));
    }
}
