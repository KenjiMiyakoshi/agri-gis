using System.ComponentModel;
using AgriGis.Desktop.Services.Import;

namespace AgriGis.Desktop.Controls;

// WB4 B405: ImportWizard Step2 で推論済みスキーマをユーザに見せ、type/required を編集してもらう UserControl。
// BindingList<InferredField> をバインドすることで Name/Type/Required の編集が
// そのまま元データに反映される。AttributeEditorControl との共用 (H4 解消) はそのインタフェース連動を伴うが、
// 既存 AttributeEditor は FlowLayoutPanel 構成のため本コントロールへの置換は Phase C 以降の改修で行う。
public partial class SchemaGrid : UserControl
{
    private static readonly string[] AllowedTypes =
        { "string", "integer", "number", "boolean", "date" };

    public SchemaGrid()
    {
        InitializeComponent();
        ConfigureGrid();
    }

    // BindingList<InferredField> を渡す。grid 内で双方向バインド。
    public void SetFields(BindingList<InferredField> fields)
    {
        grid.DataSource = fields;
    }

    private void ConfigureGrid()
    {
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;

        var nameCol = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InferredField.Name),
            HeaderText = "Name",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 30
        };
        var typeCol = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(InferredField.Type),
            HeaderText = "Type",
            DataSource = AllowedTypes,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25
        };
        var requiredCol = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(InferredField.Required),
            HeaderText = "Required",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 15
        };
        var nullableCol = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(InferredField.Nullable),
            HeaderText = "Nullable",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 15
        };

        grid.Columns.AddRange(nameCol, typeCol, requiredCol, nullableCol);
    }
}
