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

    /// <summary>drag-and-drop によるノード移動確定。</summary>
    public event EventHandler<LayerTreeNodeMovedEventArgs>? NodeMoved;

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
        var selected = (e.State & TreeNodeStates.Selected) != 0;
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
        // 展開 glyph (PlusMinus) のクリックは drag 対象にしない
        if (HitTest(e.Location).Location == TreeViewHitTestLocations.PlusMinus) return;
        var node = NodeFromRow(e.Location);
        if (node is null) return;
        SelectedNode = node;
        _dragCandidate = node;
        _dragStartPoint = e.Location;
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
        var node = _dragCandidate;
        ShowGhost(node);
        try
        {
            DoDragDrop(node, DragDropEffects.Move);
        }
        finally
        {
            HideGhost();
            ClearDropIndicator();
            _dragCandidate = null;
            _dragStarted = false;
        }
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
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src || src.TreeView != this)
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

        // group を自分自身 / 自分の子孫へ drop → 禁止
        if (target == src || (src.Tag is TreeGroupNode && IsAncestorNode(src, target)))
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

        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src || src.TreeView != this) return;

        string? parentKey;
        int index;
        switch (pos)
        {
            case LayerTreeDropPosition.Above when targetNode is not null:
                parentKey = KeyOf(targetNode.Parent);
                index = targetNode.Index;
                AdjustForSameParentMove(src, targetNode.Parent, ref index);
                break;
            case LayerTreeDropPosition.Below when targetNode is not null:
                parentKey = KeyOf(targetNode.Parent);
                index = targetNode.Index + 1;
                AdjustForSameParentMove(src, targetNode.Parent, ref index);
                break;
            case LayerTreeDropPosition.Into when targetNode?.Tag is TreeGroupNode group:
                parentKey = group.Key;
                index = targetNode.Nodes.Count; // 末尾 (model 側で detach 後に clamp される)
                break;
            case LayerTreeDropPosition.RootEnd:
                parentKey = null;
                index = Nodes.Count;
                break;
            default:
                return;
        }

        LayerTreeNodeMovedEventArgs? args = src.Tag switch
        {
            TreeLayerNode layer => LayerTreeNodeMovedEventArgs.ForLayer(layer.LayerId, parentKey, index),
            TreeGroupNode g => LayerTreeNodeMovedEventArgs.ForGroup(g.Key, parentKey, index),
            _ => null,
        };
        if (args is not null)
        {
            NodeMoved?.Invoke(this, args);
        }
    }

    // 同一親内の移動で src を抜くと src 以降の index が 1 つずれるため調整する。
    private static void AdjustForSameParentMove(TreeNode src, TreeNode? targetParent, ref int index)
    {
        if (src.Parent == targetParent && src.Index < index) index--;
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

    private void ShowGhost(TreeNode node)
    {
        _dragGhost ??= new DragGhostForm();
        _dragGhost.TextLabel.Text = $"↕  {node.Text}";
        _dragGhost.Size = new Size(
            _dragGhost.TextLabel.PreferredWidth + 4,
            _dragGhost.TextLabel.PreferredHeight + 4);
        _dragGhost.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 14);
        _dragGhost.Show();
    }

    private void HideGhost() => _dragGhost?.Hide();

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
