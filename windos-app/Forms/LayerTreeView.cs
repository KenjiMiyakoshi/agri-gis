using System.Windows.Forms.VisualStyles;
using AgriGis.Desktop.Core.LayerTree;

namespace AgriGis.Desktop.Forms;

// LG301/LG302 (Phase LG WLG3): レイヤツリー用 owner-draw TreeView。
//
// - DrawMode=OwnerDrawText + CheckBoxes=false。native checkbox は 1 個しか持てないため、
//   行の右端に CheckBoxRenderer で 3 列 (表示 / 編集 / スナップ) を自前描画する (OS テーマ追従)。
// - group 行は「表示」のみ 3 値 (Checked / Unchecked / Mixed=CheckBoxState.MixedNormal)。
// - ノードの Tag に Core の TreeGroupNode / TreeLayerNode を持たせる (モデルは in-place 更新
//   されるので、可視/フラグ変更は Invalidate() だけで再描画に反映される)。
//
// 過去サイクル (DragAwareCheckedListBox / F'304) の drag-and-drop 3 教訓を踏襲:
//   1. DragDrop では drop indicator を ClearDropIndicator() より「前」に読む
//      (クリア後に読むと常に None/-1 で並べ替えが一切走らなくなる)
//   2. checkbox トグルの native 再入を避ける — WM_LBUTTONDOWN を checkbox 矩形ヒット時に
//      native へ渡さず自前トグルのみ発火 (選択変更・展開トグルも抑止)。drag は
//      SystemInformation.DragSize の threshold を超えてから開始し、開始後はトグル不発火
//   3. drop indicator (DodgerBlue 3px ライン + ▶ 三角 / グループ内 drop は行ハイライト) は
//      WM_PAINT の base 処理後に Graphics.FromHwnd で上書き描画
internal sealed class LayerTreeView : TreeView
{
    private const int WM_PAINT = 0x000F;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    // checkbox 列: 右端からのオフセット (リサイズ追従)。LayerTreeHeaderPanel と共有する。
    internal const int VisibleColumnRight = 90;
    internal const int EditColumnRight = 60;
    internal const int SnapColumnRight = 30;
    internal const int CheckBoxSize = 16;

    private const int IndicatorThickness = 3;
    private static readonly Color IndicatorColor = Color.DodgerBlue;

    public LayerTreeView()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawText;
        CheckBoxes = false;          // 教訓 2: native CheckBoxes は使わない (owner-draw 自前 3 列)
        HideSelection = false;
        ItemHeight = Math.Max(ItemHeight, 22);
        AllowDrop = true;
        ShowNodeToolTips = false;
        _toolTip = new ToolTip();
    }

    // ---------------------------------------------------------------
    // 公開イベント (MainForm が model 操作 + bridge 通知 + 永続化を行う)
    // ---------------------------------------------------------------

    /// <summary>layer 行の「表示」checkbox クリック (layerId)。</summary>
    public event Action<int>? LayerVisibleToggled;

    /// <summary>layer 行の「編集」checkbox クリック (layerId)。将来機能、現在は状態保存のみ。</summary>
    public event Action<int>? LayerEditToggled;

    /// <summary>layer 行の「スナップ」checkbox クリック (layerId)。将来機能、現在は状態保存のみ。</summary>
    public event Action<int>? LayerSnapToggled;

    /// <summary>group 行の「表示」checkbox クリック (group key)。</summary>
    public event Action<string>? GroupVisibleToggled;

    /// <summary>drag-and-drop による単一ノード (group 含む) 移動確定。</summary>
    public event EventHandler<LayerTreeNodeMovedEventArgs>? NodeMoved;

    /// <summary>
    /// LGP302: 複数レイヤのまとめ D&D 移動確定。LayerIds は可視 DFS 順 (画面表示順)。
    /// group ノードは対象外 (layer のみ抽出済み)。
    /// </summary>
    public event EventHandler<LayerTreeLayersMovedEventArgs>? LayersMoved;

    // ---------------------------------------------------------------
    // LGP301: 複数選択 (順序付き集合 + アンカー)
    // ---------------------------------------------------------------
    //
    // native TreeView は単一選択 (SelectedNode) しか持てないため、複数選択を自前管理する。
    // - _selectedNodes: 選択ノードを「追加された順」で保持 (List)。描画/復元の安定順に使う
    // - _selectedSet: O(1) で「選択中か」を判定する HashSet (List と常に同期)
    // - native SelectedNode はフォーカス枠 (キャレット) 表示用に併用維持する
    // - _anchorNode: Shift 範囲選択の起点。単一選択 / Ctrl トグル追加で更新、Shift では固定

    private readonly List<TreeNode> _selectedNodes = new();
    private readonly HashSet<TreeNode> _selectedSet = new();
    private TreeNode? _anchorNode;

    /// <summary>現在の複数選択ノード (追加順、read only)。</summary>
    public IReadOnlyList<TreeNode> SelectedNodes => _selectedNodes;

    private void ClearMultiSelection()
    {
        _selectedNodes.Clear();
        _selectedSet.Clear();
    }

    private void SetSingleSelection(TreeNode node)
    {
        ClearMultiSelection();
        _selectedNodes.Add(node);
        _selectedSet.Add(node);
        _anchorNode = node;
    }

    private void ToggleSelection(TreeNode node)
    {
        if (_selectedSet.Remove(node))
        {
            _selectedNodes.Remove(node);
            // 除去後はアンカーを残存選択の末尾へ寄せる (無ければ null)
            if (ReferenceEquals(_anchorNode, node))
                _anchorNode = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
        }
        else
        {
            _selectedNodes.Add(node);
            _selectedSet.Add(node);
            _anchorNode = node;
        }
    }

    // Shift 範囲選択: アンカーから clicked まで「可視 DFS 順」で連続選択する。
    // 折りたたみグループの中のノードは可視列挙に現れないので自然に範囲対象外となる。
    private void SelectRange(TreeNode anchor, TreeNode clicked)
    {
        var visible = EnumerateVisibleNodesDfs().ToList();
        var range = LayerTreeSelectionMath.ComputeShiftRange(visible, anchor, clicked);
        ClearMultiSelection();
        foreach (var n in range)
        {
            _selectedNodes.Add(n);
            _selectedSet.Add(n);
        }
        // アンカーは固定 (Shift では更新しない)
    }

    /// <summary>
    /// LGP301: 可視 DFS 順 (展開グループのみ降りる、native の NextVisibleNode 順と一致) で
    /// ノードを列挙する。Shift 範囲選択と、まとめ D&D の挿入順算出に使う。
    /// </summary>
    public IEnumerable<TreeNode> EnumerateVisibleNodesDfs()
    {
        for (var n = Nodes.Count > 0 ? Nodes[0] : null; n is not null; n = n.NextVisibleNode)
        {
            yield return n;
        }
    }

    /// <summary>
    /// LGP302: 再構築後に layerId 集合で複数選択を復元する (移動したレイヤ群を選択維持)。
    /// native SelectedNode は focus 用に集合の先頭 (可視 DFS 順) へ合わせる。
    /// </summary>
    public void RestoreSelectionByLayerIds(IReadOnlyCollection<int> layerIds)
    {
        ClearMultiSelection();
        _anchorNode = null;
        if (layerIds.Count == 0)
        {
            SelectedNode = null;
            Invalidate();
            return;
        }

        var wanted = new HashSet<int>(layerIds);
        TreeNode? first = null;
        foreach (var n in EnumerateVisibleNodesDfs())
        {
            if (n.Tag is TreeLayerNode layer && wanted.Contains(layer.LayerId))
            {
                _selectedNodes.Add(n);
                _selectedSet.Add(n);
                first ??= n;
            }
        }
        _anchorNode = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
        SelectedNode = first; // focus 枠用
        Invalidate();
    }

    // ---------------------------------------------------------------
    // owner-draw (LG301)
    // ---------------------------------------------------------------

    private Font? _boldFont;
    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont?.Dispose();
        _boldFont = null;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate(); // checkbox 列は右端固定 X なのでリサイズで全再描画
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        base.OnDrawNode(e);
        if (e.Node is null || e.Node.Bounds.Height <= 0)
        {
            e.DrawDefault = true;
            return;
        }

        var g = e.Graphics;
        var rowTop = e.Node.Bounds.Top;
        var rowHeight = e.Node.Bounds.Height;
        // LGP301: 複数選択集合に含まれるノードは全て選択ハイライト。集合が空の場合のみ
        // native の Selected 状態 (選択復元前の初期描画等) にフォールバックする。
        var selected = _selectedSet.Count > 0
            ? _selectedSet.Contains(e.Node)
            : (e.State & TreeNodeStates.Selected) != 0;
        var isGroup = e.Node.Tag is TreeGroupNode;
        var font = isGroup ? BoldFont : Font;

        // テキスト (checkbox 列に被らないよう右端をクリップ)
        var maxTextRight = ClientSize.Width - VisibleColumnRight - 6;
        var textWidth = TextRenderer.MeasureText(g, e.Node.Text, font).Width;
        var textRect = new Rectangle(
            e.Bounds.X, rowTop,
            Math.Max(0, Math.Min(textWidth + 4, maxTextRight - e.Bounds.X)), rowHeight);
        if (selected && Focused)
        {
            g.FillRectangle(SystemBrushes.Highlight, textRect);
        }
        else if (selected)
        {
            g.FillRectangle(SystemBrushes.Control, textRect);
        }
        else
        {
            using var back = new SolidBrush(BackColor);
            g.FillRectangle(back, textRect);
        }
        TextRenderer.DrawText(
            g, e.Node.Text, font, textRect,
            selected && Focused ? SystemColors.HighlightText : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // checkbox 列 (CheckBoxRenderer で OS テーマ追従)
        switch (e.Node.Tag)
        {
            case TreeLayerNode layer:
                CheckBoxRenderer.DrawCheckBox(g,
                    GetCheckBoxRect(LayerTreeCheckColumn.Visible, rowTop, rowHeight).Location,
                    layer.Visible ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
                CheckBoxRenderer.DrawCheckBox(g,
                    GetCheckBoxRect(LayerTreeCheckColumn.Edit, rowTop, rowHeight).Location,
                    layer.EditEnabled ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
                CheckBoxRenderer.DrawCheckBox(g,
                    GetCheckBoxRect(LayerTreeCheckColumn.Snap, rowTop, rowHeight).Location,
                    layer.SnapEnabled ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
                break;

            case TreeGroupNode group:
                CheckBoxRenderer.DrawCheckBox(g,
                    GetCheckBoxRect(LayerTreeCheckColumn.Visible, rowTop, rowHeight).Location,
                    GetGroupBoxState(group));
                break;
        }
    }

    /// <summary>子孫レイヤの Visible 集計 → 3 値 CheckBoxState (Mixed は MixedNormal)。</summary>
    private static CheckBoxState GetGroupBoxState(TreeGroupNode group)
    {
        var total = 0;
        var visible = 0;
        Count(group);
        if (total == 0 || visible == 0) return CheckBoxState.UncheckedNormal;
        return visible == total ? CheckBoxState.CheckedNormal : CheckBoxState.MixedNormal;

        void Count(TreeGroupNode g)
        {
            foreach (var child in g.Children)
            {
                switch (child)
                {
                    case TreeLayerNode l:
                        total++;
                        if (l.Visible) visible++;
                        break;
                    case TreeGroupNode sub:
                        Count(sub);
                        break;
                }
            }
        }
    }

    private Rectangle GetCheckBoxRect(LayerTreeCheckColumn column, int rowTop, int rowHeight)
    {
        var x = column switch
        {
            LayerTreeCheckColumn.Visible => ClientSize.Width - VisibleColumnRight,
            LayerTreeCheckColumn.Edit => ClientSize.Width - EditColumnRight,
            _ => ClientSize.Width - SnapColumnRight,
        };
        return new Rectangle(x, rowTop + (rowHeight - CheckBoxSize) / 2, CheckBoxSize, CheckBoxSize);
    }

    // ---------------------------------------------------------------
    // checkbox hit-test + WM_LBUTTONDOWN 自前処理 (LG301、教訓 2)
    // ---------------------------------------------------------------

    private LayerTreeCheckColumn? HitTestCheckBox(TreeNode node, Point pt)
    {
        var bounds = node.Bounds;
        if (pt.Y < bounds.Top || pt.Y >= bounds.Bottom) return null;
        var isGroup = node.Tag is TreeGroupNode;
        foreach (var col in new[]
                 {
                     LayerTreeCheckColumn.Visible,
                     LayerTreeCheckColumn.Edit,
                     LayerTreeCheckColumn.Snap,
                 })
        {
            if (isGroup && col != LayerTreeCheckColumn.Visible) continue;
            var rect = GetCheckBoxRect(col, bounds.Top, bounds.Height);
            rect.Inflate(3, 3); // クリックしやすいよう少し拡大
            if (rect.Contains(pt)) return col;
        }
        return null;
    }

    /// <summary>Y 座標から行のノードを返す (label の右側・インデント部分も拾う)。</summary>
    private TreeNode? NodeFromRow(Point pt)
    {
        if (pt.Y < 0 || pt.Y >= ClientSize.Height) return null;
        for (var n = TopNode; n is not null; n = n.NextVisibleNode)
        {
            var b = n.Bounds;
            if (pt.Y >= b.Top && pt.Y < b.Bottom) return n;
            if (b.Top > pt.Y) break;
        }
        return null;
    }

    private void RaiseToggle(TreeNode node, LayerTreeCheckColumn column)
    {
        switch (node.Tag)
        {
            case TreeLayerNode layer:
                switch (column)
                {
                    case LayerTreeCheckColumn.Visible: LayerVisibleToggled?.Invoke(layer.LayerId); break;
                    case LayerTreeCheckColumn.Edit: LayerEditToggled?.Invoke(layer.LayerId); break;
                    case LayerTreeCheckColumn.Snap: LayerSnapToggled?.Invoke(layer.LayerId); break;
                }
                break;
            case TreeGroupNode group when column == LayerTreeCheckColumn.Visible:
                GroupVisibleToggled?.Invoke(group.Key);
                break;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // 教訓 2: checkbox 矩形のクリックは native へ渡さない (選択変更・展開トグル・
        // ダブルクリック展開の再入を全て抑止し、自前トグルイベントのみ発火する)。
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
        {
            var pt = new Point(
                unchecked((short)(long)m.LParam),
                unchecked((short)((long)m.LParam >> 16)));
            var node = NodeFromRow(pt);
            if (node is not null && HitTestCheckBox(node, pt) is { } col && !_dragStarted)
            {
                Focus();
                RaiseToggle(node, col);
                return;
            }
        }

        base.WndProc(ref m);

        // 教訓 3: drop indicator は native 描画の後に上書きする
        if (m.Msg == WM_PAINT && _dropPosition != LayerTreeDropPosition.None)
        {
            try
            {
                using var g = Graphics.FromHwnd(Handle);
                DrawDropIndicator(g);
            }
            catch
            {
                // 描画失敗は無視 (UX への影響なし、次の Invalidate で再描画)
            }
        }
    }

    // ---------------------------------------------------------------
    // 編集/スナップ checkbox の「将来機能」tooltip
    // ---------------------------------------------------------------

    private readonly ToolTip _toolTip;
    private bool _futureTipActive;
    private const string FutureFeatureTip = "将来機能 (現在は状態保存のみ)";

    private void UpdateFutureFeatureTooltip(Point pt)
    {
        var node = NodeFromRow(pt);
        var col = node is null ? null : HitTestCheckBox(node, pt);
        var overFuture = node?.Tag is TreeLayerNode &&
                         col is LayerTreeCheckColumn.Edit or LayerTreeCheckColumn.Snap;
        if (overFuture == _futureTipActive) return;
        _futureTipActive = overFuture;
        _toolTip.SetToolTip(this, overFuture ? FutureFeatureTip : null);
    }

    // ---------------------------------------------------------------
    // drag-and-drop (LG302)
    // ---------------------------------------------------------------

    private TreeNode? _dragCandidate;
    private Point _dragStartPoint;
    private bool _dragStarted;
    private DragGhostForm? _dragGhost;

    private TreeNode? _dropTargetNode;
    private LayerTreeDropPosition _dropPosition = LayerTreeDropPosition.None;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _dragCandidate = null;
        _dragStarted = false;
        if (e.Button != MouseButtons.Left) return;
        // 展開 glyph (PlusMinus) のクリックは drag / 選択対象にしない
        if (HitTest(e.Location).Location == TreeViewHitTestLocations.PlusMinus) return;
        var node = NodeFromRow(e.Location);
        if (node is null) return;

        // LGP301: 修飾キーで複数選択を更新する。checkbox 列ヒットは WndProc が
        // base.WndProc を呼ばず横取りするため、ここには到達しない (選択と独立)。
        ApplySelectionOnMouseDown(node);
        SelectedNode = node; // focus 枠 (キャレット) 用
        Invalidate();

        _dragCandidate = node;
        _dragStartPoint = e.Location;
    }

    private void ApplySelectionOnMouseDown(TreeNode node)
    {
        var ctrl = (ModifierKeys & Keys.Control) != 0;
        var shift = (ModifierKeys & Keys.Shift) != 0;
        if (shift && _anchorNode is not null)
        {
            SelectRange(_anchorNode, node);
        }
        else if (ctrl)
        {
            ToggleSelection(node);
        }
        else
        {
            SetSingleSelection(node);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateFutureFeatureTooltip(e.Location);
        if (e.Button != MouseButtons.Left || _dragCandidate is null || _dragStarted) return;
        // SystemInformation.DragSize の閾値を超えたら drag 開始 (click / toggle と区別)
        var dx = Math.Abs(e.X - _dragStartPoint.X);
        var dy = Math.Abs(e.Y - _dragStartPoint.Y);
        if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;

        _dragStarted = true;
        var grabbed = _dragCandidate;

        // LGP302: 掴んだノードが選択集合に含まれなければ単一リセット (掴んだノードのみ選択)。
        if (!_selectedSet.Contains(grabbed))
        {
            SetSingleSelection(grabbed);
            SelectedNode = grabbed;
            Invalidate();
        }

        var payload = BuildDragPayload(grabbed);
        if (payload is null)
        {
            // 対象 layer が 0 件 (group のみ複数選択など) → drag キャンセル
            _dragCandidate = null;
            _dragStarted = false;
            return;
        }

        ShowGhost(payload, grabbed);
        try
        {
            DoDragDrop(payload, DragDropEffects.Move);
        }
        finally
        {
            HideGhost();
            ClearDropIndicator();
            _dragCandidate = null;
            _dragStarted = false;
        }
    }

    // 掴んだノードから drag payload を決める。
    // - 選択集合に layer ノードが 1 つ以上あれば layer のみ抽出した「まとめ移動」 (group は除外)
    // - 集合が単一の group ノードのみなら従来の単独 group 移動
    // - layer も group も無ければ null (drag キャンセル)
    private LayerDragPayload? BuildDragPayload(TreeNode grabbed)
    {
        // 可視 DFS 順 (= 画面表示順) で選択中の layer ノードを並べる。
        // PLAN の「選択時の相対順を保ったまま」を画面表示順と解釈する (連続挿入が直感的)。
        var layerIds = new List<int>();
        foreach (var n in EnumerateVisibleNodesDfs())
        {
            if (_selectedSet.Contains(n) && n.Tag is TreeLayerNode layer)
                layerIds.Add(layer.LayerId);
        }
        if (layerIds.Count > 0) return LayerDragPayload.ForLayers(this, layerIds);

        // layer が一つも無い → 単独 group 移動 (掴んだノードが group のときのみ)
        if (grabbed.Tag is TreeGroupNode g) return LayerDragPayload.ForGroup(this, g.Key, grabbed);
        return null;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragCandidate = null;
        _dragStarted = false;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (GetDragPayload(e) is not { } payload)
        {
            e.Effect = DragDropEffects.None;
            ClearDropIndicator();
            return;
        }

        var pt = PointToClient(new Point(e.X, e.Y));
        var target = NodeFromRow(pt);
        if (target is null)
        {
            // 全ノードより下の空白 → ルート末尾へ
            e.Effect = DragDropEffects.Move;
            SetDropIndicator(null, LayerTreeDropPosition.RootEnd);
            return;
        }

        // 単独 group 移動のみ: 自分自身 / 自分の子孫へ drop → 禁止。
        // まとめ layer 移動は layer 限定なのでこの制約は無関係 (group は対象外)。
        if (payload.GroupNode is { } srcGroup &&
            (target == srcGroup || IsAncestorNode(srcGroup, target)))
        {
            e.Effect = DragDropEffects.None;
            ClearDropIndicator();
            return;
        }

        var bounds = RowBounds(target);
        LayerTreeDropPosition pos;
        if (target.Tag is TreeGroupNode)
        {
            // group 行: 上 1/3 = above, 中央 1/3 = グループ内へ, 下 1/3 = below
            var third = Math.Max(1, bounds.Height / 3);
            pos = pt.Y < bounds.Top + third ? LayerTreeDropPosition.Above
                : pt.Y >= bounds.Bottom - third ? LayerTreeDropPosition.Below
                : LayerTreeDropPosition.Into;
        }
        else
        {
            // layer 行: 上半分 = above, 下半分 = below
            pos = pt.Y < bounds.Top + bounds.Height / 2
                ? LayerTreeDropPosition.Above
                : LayerTreeDropPosition.Below;
        }

        e.Effect = DragDropEffects.Move;
        SetDropIndicator(target, pos);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        ClearDropIndicator();
    }

    protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);
        e.UseDefaultCursors = false;
        Cursor.Current = Cursors.Hand;
        if (_dragGhost is { Visible: true })
        {
            _dragGhost.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 14);
        }
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        // 教訓 1: drop 位置 (indicator) は ClearDropIndicator() より「前」に読む
        var targetNode = _dropTargetNode;
        var pos = _dropPosition;
        ClearDropIndicator();

        if (GetDragPayload(e) is not { } payload) return;

        // drop 先の parentKey + 挿入先 parent ノード (null=ルート) + UI 上のベース挿入 index
        // (= 移動元を抜く前の挿入位置) を算出する。
        // dropParentNode は「挿入される親 TreeNode」: Above/Below は target の親、Into は target 自身、
        // RootEnd は null。targetSiblings は対応する子コレクション。
        string? parentKey;
        TreeNode? dropParentNode;
        TreeNodeCollection targetSiblings;
        int baseIndex;
        var insertBetween = false; // Above/Below のみ true (同一親内の src 抽出補正対象)
        switch (pos)
        {
            case LayerTreeDropPosition.Above when targetNode is not null:
                parentKey = KeyOf(targetNode.Parent);
                dropParentNode = targetNode.Parent;
                targetSiblings = targetNode.Parent?.Nodes ?? Nodes;
                baseIndex = targetNode.Index;
                insertBetween = true;
                break;
            case LayerTreeDropPosition.Below when targetNode is not null:
                parentKey = KeyOf(targetNode.Parent);
                dropParentNode = targetNode.Parent;
                targetSiblings = targetNode.Parent?.Nodes ?? Nodes;
                baseIndex = targetNode.Index + 1;
                insertBetween = true;
                break;
            case LayerTreeDropPosition.Into when targetNode?.Tag is TreeGroupNode group:
                parentKey = group.Key;
                dropParentNode = targetNode;
                targetSiblings = targetNode.Nodes;
                baseIndex = targetNode.Nodes.Count; // 末尾
                break;
            case LayerTreeDropPosition.RootEnd:
                parentKey = null;
                dropParentNode = null;
                targetSiblings = Nodes;
                baseIndex = Nodes.Count;
                break;
            default:
                return;
        }

        if (payload.GroupNode is { } srcGroup)
        {
            // 単独 group 移動 (従来経路): Above/Below で同一親内なら src 抽出ぶん index を 1 詰める。
            var index = baseIndex;
            if (insertBetween && srcGroup.Parent == dropParentNode && srcGroup.Index < index)
            {
                index--;
            }
            NodeMoved?.Invoke(this,
                LayerTreeNodeMovedEventArgs.ForGroup(payload.GroupKey!, parentKey, index));
            return;
        }

        // まとめ layer 移動: ベース index を「移動元 layer 抽出後」の startOrder に変換する。
        // Core MoveLayers は targets を detach 後に startOrder を clamp して連続挿入するため、
        // 同一 parent 内に居て baseIndex より前にある移動対象の数だけ前詰めする。
        // TreeNodeCollection は IReadOnlyList<TreeNode> 非実装なので一旦 list 化して渡す。
        var siblingList = targetSiblings.Cast<TreeNode>().ToList();
        var startOrder = LayerTreeSelectionMath.ComputeMultiStartOrder(siblingList, baseIndex, _selectedSet);
        LayersMoved?.Invoke(this,
            new LayerTreeLayersMovedEventArgs(payload.LayerIds!, parentKey, startOrder));
    }

    private static string? KeyOf(TreeNode? node)
        => node?.Tag is TreeGroupNode g ? g.Key : null;

    /// <summary>node が ancestor の (真の) 子孫かどうか。</summary>
    private static bool IsAncestorNode(TreeNode ancestor, TreeNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
        {
            if (p == ancestor) return true;
        }
        return false;
    }

    private Rectangle RowBounds(TreeNode node)
        => new(0, node.Bounds.Top, ClientSize.Width, node.Bounds.Height);

    // ---------------------------------------------------------------
    // drop indicator (教訓 3)
    // ---------------------------------------------------------------

    private void SetDropIndicator(TreeNode? node, LayerTreeDropPosition pos)
    {
        if (_dropTargetNode == node && _dropPosition == pos) return;
        _dropTargetNode = node;
        _dropPosition = pos;
        Invalidate();
    }

    private void ClearDropIndicator()
    {
        if (_dropPosition == LayerTreeDropPosition.None) return;
        _dropTargetNode = null;
        _dropPosition = LayerTreeDropPosition.None;
        Invalidate();
    }

    private void DrawDropIndicator(Graphics g)
    {
        switch (_dropPosition)
        {
            case LayerTreeDropPosition.Above when _dropTargetNode is not null:
                DrawInsertLine(g, RowBounds(_dropTargetNode).Top + 1);
                break;
            case LayerTreeDropPosition.Below when _dropTargetNode is not null:
                DrawInsertLine(g, RowBounds(_dropTargetNode).Bottom - 1);
                break;
            case LayerTreeDropPosition.Into when _dropTargetNode is not null:
            {
                // グループ内へ drop: 行全体を青枠 + 半透明ハイライト
                var b = RowBounds(_dropTargetNode);
                using var fill = new SolidBrush(Color.FromArgb(40, IndicatorColor));
                g.FillRectangle(fill, b);
                using var pen = new Pen(IndicatorColor, 2);
                g.DrawRectangle(pen, 1, b.Top, ClientSize.Width - 3, b.Height - 1);
                break;
            }
            case LayerTreeDropPosition.RootEnd:
            {
                var y = 2;
                var last = LastDisplayedNode();
                if (last is not null) y = Math.Min(last.Bounds.Bottom - 1, ClientSize.Height - 2);
                DrawInsertLine(g, y);
                break;
            }
        }
    }

    private void DrawInsertLine(Graphics g, int y)
    {
        using var pen = new Pen(IndicatorColor, IndicatorThickness);
        g.DrawLine(pen, 2, y, ClientSize.Width - 4, y);
        // 端に小さな矢印 (▶) を描画して目を引く (DragAwareCheckedListBox 流用)
        using var brush = new SolidBrush(IndicatorColor);
        g.FillPolygon(brush, new[]
        {
            new Point(2, y - 4),
            new Point(8, y),
            new Point(2, y + 4),
        });
    }

    private TreeNode? LastDisplayedNode()
    {
        TreeNode? last = null;
        for (var n = TopNode; n is not null; n = n.NextVisibleNode)
        {
            last = n;
            if (n.Bounds.Bottom >= ClientSize.Height) break;
        }
        return last;
    }

    // ---------------------------------------------------------------
    // drag ghost (F'304 の DragGhostForm を移設)
    // ---------------------------------------------------------------

    private void ShowGhost(LayerDragPayload payload, TreeNode grabbed)
    {
        _dragGhost ??= new DragGhostForm();
        // LGP302: layer N>1 件なら「N 件のレイヤ」、1 件 / group なら従来どおりノード名。
        var label = payload.LayerIds is { Count: > 1 } ids
            ? $"{ids.Count} 件のレイヤ"
            : grabbed.Text;
        _dragGhost.TextLabel.Text = $"↕  {label}";
        _dragGhost.Size = new Size(
            _dragGhost.TextLabel.PreferredWidth + 4,
            _dragGhost.TextLabel.PreferredHeight + 4);
        _dragGhost.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 14);
        _dragGhost.Show();
    }

    // drag データから自分自身が作った payload を取り出す (他コントロール由来は null)。
    private LayerDragPayload? GetDragPayload(DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(LayerDragPayload)) is LayerDragPayload p && p.Owner == this)
            return p;
        return null;
    }

    private void HideGhost() => _dragGhost?.Hide();

    // drag-and-drop の搬送オブジェクト。
    // - LayerIds 非 null = まとめ layer 移動 (可視 DFS 順)。GroupKey/GroupNode は null
    // - GroupKey 非 null = 単独 group 移動。LayerIds は null
    // Owner で自コントロール由来の drag だけを受け付ける (旧 src.TreeView 比較の置換)。
    private sealed class LayerDragPayload
    {
        private LayerDragPayload(
            LayerTreeView owner, IReadOnlyList<int>? layerIds, string? groupKey, TreeNode? groupNode)
        {
            Owner = owner;
            LayerIds = layerIds;
            GroupKey = groupKey;
            GroupNode = groupNode;
        }

        public LayerTreeView Owner { get; }
        public IReadOnlyList<int>? LayerIds { get; }
        public string? GroupKey { get; }
        public TreeNode? GroupNode { get; }

        public static LayerDragPayload ForLayers(LayerTreeView owner, IReadOnlyList<int> layerIds)
            => new(owner, layerIds, null, null);

        public static LayerDragPayload ForGroup(LayerTreeView owner, string groupKey, TreeNode node)
            => new(owner, null, groupKey, node);
    }

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
                ForeColor = Color.Black,
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _toolTip.Dispose();
            _dragGhost?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>checkbox 列の種別。</summary>
internal enum LayerTreeCheckColumn
{
    Visible,
    Edit,
    Snap,
}

/// <summary>drop indicator の位置種別。</summary>
internal enum LayerTreeDropPosition
{
    None,
    Above,
    Below,
    Into,
    RootEnd,
}

/// <summary>
/// NodeMoved イベント引数。LayerId / GroupKey のどちらか一方が設定される。
/// TargetParentKey=null はルート直下、TargetIndex は移動元を除いた後の挿入位置。
/// </summary>
internal sealed class LayerTreeNodeMovedEventArgs : EventArgs
{
    private LayerTreeNodeMovedEventArgs(int? layerId, string? groupKey, string? targetParentKey, int targetIndex)
    {
        LayerId = layerId;
        GroupKey = groupKey;
        TargetParentKey = targetParentKey;
        TargetIndex = targetIndex;
    }

    public static LayerTreeNodeMovedEventArgs ForLayer(int layerId, string? parentKey, int index)
        => new(layerId, null, parentKey, index);

    public static LayerTreeNodeMovedEventArgs ForGroup(string groupKey, string? parentKey, int index)
        => new(null, groupKey, parentKey, index);

    public int? LayerId { get; }
    public string? GroupKey { get; }
    public string? TargetParentKey { get; }
    public int TargetIndex { get; }
}

/// <summary>
/// LGP302: まとめ layer D&D の移動確定イベント引数。
/// LayerIds は可視 DFS 順 (画面表示順)。TargetParentKey=null はルート直下。
/// StartOrder は移動元 layer を抜いた後の挿入起点 (Core MoveLayers の startOrder にそのまま渡す)。
/// </summary>
internal sealed class LayerTreeLayersMovedEventArgs : EventArgs
{
    public LayerTreeLayersMovedEventArgs(
        IReadOnlyList<int> layerIds, string? targetParentKey, int startOrder)
    {
        LayerIds = layerIds;
        TargetParentKey = targetParentKey;
        StartOrder = startOrder;
    }

    public IReadOnlyList<int> LayerIds { get; }
    public string? TargetParentKey { get; }
    public int StartOrder { get; }
}

// LG301: ツリー上部の列ヘッダ ("表示 編集 スナップ")。
// LayerTreeView の右端固定列と同じオフセットでラベルを checkbox 中心に揃えて描画する。
internal sealed class LayerTreeHeaderPanel : Panel
{
    private Font? _headerFont;

    public LayerTreeHeaderPanel()
    {
        Height = 20;
        DoubleBuffered = true;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _headerFont ??= new Font(Font.FontFamily, 8f);
        var g = e.Graphics;
        DrawLabel(g, "表示", LayerTreeView.VisibleColumnRight);
        DrawLabel(g, "編集", LayerTreeView.EditColumnRight);
        DrawLabel(g, "スナップ", LayerTreeView.SnapColumnRight);
        using var pen = new Pen(SystemColors.ControlDark);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    private void DrawLabel(Graphics g, string text, int columnRight)
    {
        var centerX = ClientSize.Width - columnRight + LayerTreeView.CheckBoxSize / 2;
        var size = TextRenderer.MeasureText(g, text, _headerFont);
        TextRenderer.DrawText(
            g, text, _headerFont,
            new Point(centerX - size.Width / 2, (Height - size.Height) / 2),
            SystemColors.ControlText);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
