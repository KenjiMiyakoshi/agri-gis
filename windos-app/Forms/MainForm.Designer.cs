#nullable enable

using Microsoft.Web.WebView2.WinForms;

namespace AgriGis.Desktop.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private WebView2 webView = null!;
    private Panel rightPanel = null!;
    private Label layerLabel = null!;
    // F301 (Phase F WF3): 複数 layer を同時 ON/OFF できる CheckedListBox に変更
    // 旧 layerCombo (ComboBox) は WD4 まで「単一選択」だったが、F 以降は複数表示が前提
    // F'304 hotfix: drop indicator 描画用に DragAwareCheckedListBox (CheckedListBox 派生) に置換
    internal DragAwareCheckedListBox layerList = null!;
    private AttributeEditorControl attributeEditor = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private MenuStrip menuStrip = null!;
    internal ToolStripMenuItem layerAdminMenuItem = null!;
    // E402 (WE4): asOf 過去時点モード切替
    private Panel asOfPanel = null!;
    private CheckBox asOfEnabled = null!;
    private DateTimePicker asOfPicker = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        webView = new WebView2();
        rightPanel = new Panel();
        layerLabel = new Label();
        layerList = new DragAwareCheckedListBox();
        attributeEditor = new AttributeEditorControl();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        // E402 (WE4): asOf 過去時点モード切替
        asOfPanel = new Panel();
        asOfEnabled = new CheckBox();
        asOfPicker = new DateTimePicker();
        // WB4 B406: 管理 → レイヤ管理 メニュー (admin のみ Visible=true)
        menuStrip = new MenuStrip();
        var adminMenu = new ToolStripMenuItem("管理");
        layerAdminMenuItem = new ToolStripMenuItem("レイヤ管理...");
        adminMenu.DropDownItems.Add(layerAdminMenuItem);
        menuStrip.Items.Add(adminMenu);

        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        rightPanel.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // layerLabel
        layerLabel.Dock = DockStyle.Top;
        layerLabel.Text = "表示レイヤ:";
        layerLabel.Height = 20;
        layerLabel.AutoSize = false;

        // F301 (Phase F WF3): layerList — 複数 ON/OFF 用 CheckedListBox
        // F'304 (Phase F' WF'3): drag-and-drop で z-order 並べ替え可能に
        layerList.Dock = DockStyle.Top;
        layerList.Height = 180;
        // F'304 hotfix-3: CheckOnClick=true (native の 1-click toggle に任せる)。
        //   drag 中の native ItemCheck は ItemCheck ハンドラ側で _dragStarted を見て
        //   キャンセルする方式に変更。MouseUp 内 SetItemChecked の再入問題を回避。
        layerList.CheckOnClick = true;
        layerList.IntegralHeight = false;
        layerList.AllowDrop = true;          // F'304: drag-and-drop 受領

        // attributeEditor
        attributeEditor.Dock = DockStyle.Fill;

        // E402 (WE4): asOf パネル (CheckBox + DateTimePicker)
        asOfPanel.Dock = DockStyle.Top;
        asOfPanel.Height = 32;
        asOfPanel.Padding = new Padding(0, 4, 0, 4);
        asOfEnabled.Text = "過去時点";
        asOfEnabled.Dock = DockStyle.Left;
        asOfEnabled.Width = 100;
        asOfEnabled.AutoSize = false;
        asOfEnabled.Checked = false;
        asOfPicker.Dock = DockStyle.Fill;
        asOfPicker.Format = DateTimePickerFormat.Custom;
        asOfPicker.CustomFormat = "yyyy-MM-dd";
        asOfPicker.Enabled = false;
        asOfPanel.Controls.Add(asOfPicker);
        asOfPanel.Controls.Add(asOfEnabled);

        // rightPanel (固定幅 360px、右ドック)
        rightPanel.Dock = DockStyle.Right;
        rightPanel.Width = 360;
        rightPanel.Padding = new Padding(8);
        // 追加順は「後追加が先ドック」: attributeEditor(Fill) を先に、その上に layerList, さらに上に label, asOfPanel
        rightPanel.Controls.Add(attributeEditor);
        rightPanel.Controls.Add(layerList);
        rightPanel.Controls.Add(layerLabel);
        rightPanel.Controls.Add(asOfPanel);

        // webView (残りスペースを Fill)
        webView.Dock = DockStyle.Fill;

        // statusStrip
        statusStrip.Items.Add(statusLabel);
        statusLabel.Text = "Ready";

        // MainForm
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1200, 800);

        // Controls.Add の順序：後で追加した方が先にドック領域を取る。
        // statusStrip(Bottom) → rightPanel(Right) → webView(Fill remaining) になるよう、
        // webView を最初に、最後に statusStrip を追加する。
        Controls.Add(webView);
        Controls.Add(rightPanel);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        Name = "MainForm";
        Text = "AgriGis";

        ((System.ComponentModel.ISupportInitialize)webView).EndInit();
        rightPanel.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
