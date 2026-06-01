#nullable enable

using Microsoft.Web.WebView2.WinForms;

namespace AgriGis.Desktop.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private SplitContainer splitContainer = null!;
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
        splitContainer = new SplitContainer();
        webView = new WebView2();
        rightPanel = new Panel();
        layerLabel = new Label();
        layerCombo = new ComboBox();
        attributeEditor = new AttributeEditorControl();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();

        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        ((System.ComponentModel.ISupportInitialize)webView).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        rightPanel.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // splitContainer
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.SplitterDistance = 840;
        splitContainer.Panel1.Controls.Add(webView);
        splitContainer.Panel2.Controls.Add(rightPanel);

        // webView
        webView.Dock = DockStyle.Fill;

        // rightPanel
        rightPanel.Dock = DockStyle.Fill;
        rightPanel.Padding = new Padding(8);
        rightPanel.Controls.Add(attributeEditor);
        rightPanel.Controls.Add(layerCombo);
        rightPanel.Controls.Add(layerLabel);

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

        // statusStrip
        statusStrip.Items.Add(statusLabel);
        statusLabel.Text = "Ready";

        // MainForm
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1200, 800);
        Controls.Add(splitContainer);
        Controls.Add(statusStrip);
        Name = "MainForm";
        Text = "AgriGis";

        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)webView).EndInit();
        rightPanel.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
