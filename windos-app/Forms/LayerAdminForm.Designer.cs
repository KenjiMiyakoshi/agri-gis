#nullable enable

namespace AgriGis.Desktop.Forms;

partial class LayerAdminForm
{
    private System.ComponentModel.IContainer? components = null;
    private DataGridView grid = null!;
    private ToolStrip toolStrip = null!;
    private ToolStripButton importButton = null!;
    private ToolStripButton deleteButton = null!;
    private ToolStripButton refreshButton = null!;
    private ToolStripButton closeButton = null!;
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
        toolStrip = new ToolStrip();
        importButton = new ToolStripButton();
        deleteButton = new ToolStripButton();
        refreshButton = new ToolStripButton();
        closeButton = new ToolStripButton();
        grid = new DataGridView();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();

        ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
        toolStrip.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        importButton.Text = "新規インポート";
        importButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        deleteButton.Text = "削除";
        deleteButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        refreshButton.Text = "再読込";
        refreshButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        closeButton.Text = "閉じる";
        closeButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        closeButton.Alignment = ToolStripItemAlignment.Right;
        toolStrip.Items.AddRange(new ToolStripItem[]
        { importButton, deleteButton, refreshButton, closeButton });
        toolStrip.Dock = DockStyle.Top;

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = SystemColors.Window;

        statusStrip.Items.Add(statusLabel);
        statusLabel.Text = "";

        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(800, 480);
        Controls.Add(grid);
        Controls.Add(statusStrip);
        Controls.Add(toolStrip);
        Name = "LayerAdminForm";
        Text = "レイヤ管理";
        StartPosition = FormStartPosition.CenterParent;

        ((System.ComponentModel.ISupportInitialize)grid).EndInit();
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
