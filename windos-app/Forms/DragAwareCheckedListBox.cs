using System.Drawing;
using System.Windows.Forms;

namespace AgriGis.Desktop.Forms;

// F'304 hotfix (Phase F' WF'3 polish): drop indicator (青いライン) を描画できる CheckedListBox。
//   - SetDropIndicator(index, above): 指定 item の上 / 下に青ラインを描画
//   - ClearDropIndicator(): ラインを消す
//   - WM_PAINT を override して native CheckedListBox の描画 _後_ に上書き
internal sealed class DragAwareCheckedListBox : CheckedListBox
{
    private const int WM_PAINT = 0x000F;
    private const int LINE_THICKNESS = 3;
    private static readonly Color IndicatorColor = Color.DodgerBlue;

    private int _dropIndicatorIndex = -1;
    private bool _dropAbove = true;

    public void SetDropIndicator(int index, bool above)
    {
        if (_dropIndicatorIndex == index && _dropAbove == above) return;
        _dropIndicatorIndex = index;
        _dropAbove = above;
        Invalidate();
    }

    public void ClearDropIndicator()
    {
        if (_dropIndicatorIndex < 0) return;
        _dropIndicatorIndex = -1;
        Invalidate();
    }

    /// <summary>
    /// 現在の drop indicator から「挿入先 index」を返す。
    /// above=true なら index、above=false なら index+1。Items.Count なら末尾。
    /// </summary>
    public int GetDropTargetIndex()
    {
        if (_dropIndicatorIndex < 0) return -1;
        if (_dropIndicatorIndex >= Items.Count) return Items.Count;
        return _dropAbove ? _dropIndicatorIndex : _dropIndicatorIndex + 1;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT && _dropIndicatorIndex >= 0)
        {
            try
            {
                using var g = Graphics.FromHwnd(Handle);
                DrawIndicator(g);
            }
            catch
            {
                // 描画失敗は無視 (UX への影響なし、次の Invalidate で再描画)
            }
        }
    }

    private void DrawIndicator(Graphics g)
    {
        int y;
        if (_dropIndicatorIndex >= Items.Count)
        {
            if (Items.Count == 0) return;
            var r = GetItemRectangle(Items.Count - 1);
            y = r.Bottom - 1;
        }
        else
        {
            var r = GetItemRectangle(_dropIndicatorIndex);
            y = _dropAbove ? r.Top : r.Bottom - 1;
        }
        using var pen = new Pen(IndicatorColor, LINE_THICKNESS);
        g.DrawLine(pen, 2, y, ClientSize.Width - 4, y);
        // 端に小さな矢印 (▶) を描画して目を引く
        using var brush = new SolidBrush(IndicatorColor);
        var arrowPts = new Point[]
        {
            new Point(2, y - 4),
            new Point(8, y),
            new Point(2, y + 4)
        };
        g.FillPolygon(brush, arrowPts);
    }
}
