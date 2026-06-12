#nullable enable

namespace AgriGis.Desktop.Forms;

partial class OrgPermissionsForm
{
    private System.ComponentModel.IContainer? components = null;
    private Panel topPanel = null!;
    private Label orgLabel = null!;
    private ComboBox orgCombo = null!;
    private DataGridView permGrid = null!;
    private Panel buttonPanel = null!;
    private Button saveButton = null!;
    private Button closeButton = null!;
    private Label statusLabel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        topPanel = new Panel();
        orgLabel = new Label();
        orgCombo = new ComboBox();
        permGrid = new DataGridView();
        buttonPanel = new Panel();
        saveButton = new Button();
        closeButton = new Button();
        statusLabel = new Label();

        ((System.ComponentModel.ISupportInitialize)permGrid).BeginInit();
        topPanel.SuspendLayout();
        buttonPanel.SuspendLayout();
        SuspendLayout();

        // topPanel
        topPanel.Dock = DockStyle.Top;
        topPanel.Height = 40;
        topPanel.Padding = new Padding(8, 8, 8, 8);
        orgLabel.Text = "組織:";
        orgLabel.Dock = DockStyle.Left;
        orgLabel.Width = 60;
        orgLabel.AutoSize = false;
        orgLabel.TextAlign = ContentAlignment.MiddleLeft;
        orgCombo.Dock = DockStyle.Fill;
        orgCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        topPanel.Controls.Add(orgCombo);
        topPanel.Controls.Add(orgLabel);

        // permGrid: LayerName / LayerType / CanView / CanEdit
        permGrid.Dock = DockStyle.Fill;
        permGrid.AllowUserToAddRows = false;
        permGrid.AllowUserToDeleteRows = false;
        permGrid.AllowUserToResizeRows = false;
        permGrid.RowHeadersVisible = false;
        permGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        permGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        permGrid.MultiSelect = false;

        var colLayerId = new DataGridViewTextBoxColumn
        {
            Name = "layerId",
            HeaderText = "ID",
            ReadOnly = true,
            FillWeight = 8
        };
        var colName = new DataGridViewTextBoxColumn
        {
            Name = "layerName",
            HeaderText = "レイヤ名",
            ReadOnly = true,
            FillWeight = 40
        };
        var colType = new DataGridViewTextBoxColumn
        {
            Name = "layerType",
            HeaderText = "種別",
            ReadOnly = true,
            FillWeight = 15
        };
        var colView = new DataGridViewCheckBoxColumn
        {
            Name = "canView",
            HeaderText = "閲覧可",
            FillWeight = 15
        };
        var colEdit = new DataGridViewCheckBoxColumn
        {
            Name = "canEdit",
            HeaderText = "編集可",
            FillWeight = 22
        };
        permGrid.Columns.AddRange(colLayerId, colName, colType, colView, colEdit);

        // statusLabel (中段、grid と buttonPanel の間)
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 24;
        statusLabel.Padding = new Padding(8, 4, 8, 4);
        statusLabel.AutoSize = false;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Text = "";

        // buttonPanel
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.Height = 44;
        buttonPanel.Padding = new Padding(8);
        saveButton.Text = "保存";
        saveButton.Dock = DockStyle.Right;
        saveButton.Width = 96;
        closeButton.Text = "閉じる";
        closeButton.Dock = DockStyle.Right;
        closeButton.Width = 96;
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(closeButton);

        // OrgPermissionsForm
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(720, 480);
        // 追加順 (後追加が先 Dock): permGrid(Fill) を先、その下に statusLabel, buttonPanel、上に topPanel
        Controls.Add(permGrid);
        Controls.Add(statusLabel);
        Controls.Add(buttonPanel);
        Controls.Add(topPanel);
        Text = "組織×レイヤ 権限管理";
        MinimumSize = new System.Drawing.Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;

        ((System.ComponentModel.ISupportInitialize)permGrid).EndInit();
        topPanel.ResumeLayout(false);
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
