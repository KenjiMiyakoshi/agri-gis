#nullable enable

using Microsoft.Web.WebView2.WinForms;

namespace AgriGis.Desktop.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private WebView2 webView = null!;
    private Panel rightPanel = null!;
    private Label layerLabel = null!;
    private ComboBox layerCombo = null!;
    private AttributeEditorControl attributeEditor = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;

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
        layerCombo = new ComboBox();
        attributeEditor = new AttributeEditorControl();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();

        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        rightPanel.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // layerLabel
        layerLabel.Dock = DockStyle.Top;
        layerLabel.Text = "Layer:";
        layerLabel.Height = 20;
        layerLabel.AutoSize = false;

        // layerCombo
        layerCombo.Dock = DockStyle.Top;
        layerCombo.DropDownStyle = ComboBoxStyle.DropDownList;

        // attributeEditor
        attributeEditor.Dock = DockStyle.Fill;

        // rightPanel (固定幅 360px、右ドック)
        rightPanel.Dock = DockStyle.Right;
        rightPanel.Width = 360;
        rightPanel.Padding = new Padding(8);
        // 追加順は「後追加が先ドック」: attributeEditor(Fill) を先に、その上に combo, さらに上に label
        rightPanel.Controls.Add(attributeEditor);
        rightPanel.Controls.Add(layerCombo);
        rightPanel.Controls.Add(layerLabel);

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
