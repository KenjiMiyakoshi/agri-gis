#nullable enable

namespace AgriGis.Desktop.Controls;

partial class SchemaGrid
{
    private System.ComponentModel.IContainer? components = null;
    private DataGridView grid = null!;

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
        grid = new DataGridView();
        ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
        SuspendLayout();

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.BackgroundColor = SystemColors.Window;

        Controls.Add(grid);
        Name = "SchemaGrid";
        Size = new System.Drawing.Size(600, 240);

        ((System.ComponentModel.ISupportInitialize)grid).EndInit();
        ResumeLayout(false);
    }
}
