#nullable enable

namespace AgriGis.Desktop.Forms;

partial class AttributeEditorControl
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel fieldsLayout = null!;
    private FlowLayoutPanel buttonsPanel = null!;
    private Button saveButton = null!;
    private Label headerLabel = null!;
    private Label errorLabel = null!;

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
        fieldsLayout = new TableLayoutPanel();
        buttonsPanel = new FlowLayoutPanel();
        saveButton = new Button();
        headerLabel = new Label();
        errorLabel = new Label();

        SuspendLayout();

        // headerLabel
        headerLabel.Dock = DockStyle.Top;
        headerLabel.Text = "Select a feature on the map.";
        headerLabel.Height = 24;
        headerLabel.AutoSize = false;
        headerLabel.Padding = new Padding(0, 4, 0, 4);

        // fieldsLayout
        fieldsLayout.Dock = DockStyle.Fill;
        fieldsLayout.ColumnCount = 2;
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        fieldsLayout.AutoScroll = true;

        // errorLabel
        errorLabel.Dock = DockStyle.Bottom;
        errorLabel.ForeColor = System.Drawing.Color.Red;
        errorLabel.AutoSize = false;
        errorLabel.Height = 60;

        // buttonsPanel
        buttonsPanel.Dock = DockStyle.Bottom;
        buttonsPanel.Height = 40;
        buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonsPanel.Padding = new Padding(4);
        buttonsPanel.Controls.Add(saveButton);

        // saveButton
        saveButton.Text = "Save";
        saveButton.AutoSize = true;
        saveButton.Enabled = false;

        // UserControl
        Controls.Add(fieldsLayout);
        Controls.Add(errorLabel);
        Controls.Add(buttonsPanel);
        Controls.Add(headerLabel);

        ResumeLayout(false);
    }
}
