namespace AgriGis.Desktop.Core.LayerTree;

// LGP301/LGP302 (Phase LG' WLGP3): 複数選択 D&D の純粋計算ロジック (UI 非依存)。
//
// LayerTreeView (WinForms) は TreeNode で呼ぶが、本体はジェネリックで TreeNode に依存しない。
// これにより windos-app.tests から WinForms 参照なしで Shift 範囲計算 / startOrder 算出を検証できる。
// 比較は参照同一性 (ReferenceEqualityComparer) で行う — TreeNode は値等価を持たないため。
public static class LayerTreeSelectionMath
{
    /// <summary>
    /// 可視ノード列 visible 上で anchor..clicked の連続範囲を可視順で返す。
    /// - clicked が列に無い → 空
    /// - anchor が null / 列に無い → clicked 単独
    /// - それ以外 → min(anchorIdx,clickedIdx)..max を可視順で
    /// </summary>
    public static IReadOnlyList<T> ComputeShiftRange<T>(
        IReadOnlyList<T> visible, T? anchor, T clicked)
        where T : class
    {
        var clickedIdx = IndexOfRef(visible, clicked);
        if (clickedIdx < 0) return Array.Empty<T>();
        var anchorIdx = anchor is null ? -1 : IndexOfRef(visible, anchor);
        if (anchorIdx < 0) return new[] { clicked };

        var lo = Math.Min(anchorIdx, clickedIdx);
        var hi = Math.Max(anchorIdx, clickedIdx);
        var result = new List<T>(hi - lo + 1);
        for (var i = lo; i <= hi; i++) result.Add(visible[i]);
        return result;
    }

    /// <summary>
    /// 移動先 parent の子リスト siblings と UI ベース挿入位置 baseIndex から、移動対象 (selected) を
    /// detach した後の startOrder を計算する。siblings 中 index &lt; baseIndex かつ selected な
    /// ノード数だけ baseIndex を前詰めする (Core MoveLayers が detach 後に startOrder を clamp する前提)。
    /// </summary>
    public static int ComputeMultiStartOrder<T>(
        IReadOnlyList<T> siblings, int baseIndex, ISet<T> selected)
        where T : class
    {
        var removedBefore = 0;
        var limit = Math.Min(baseIndex, siblings.Count);
        for (var i = 0; i < limit; i++)
        {
            if (selected.Contains(siblings[i])) removedBefore++;
        }
        return Math.Max(0, baseIndex - removedBefore);
    }

    private static int IndexOfRef<T>(IReadOnlyList<T> list, T node) where T : class
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], node)) return i;
        return -1;
    }
}
