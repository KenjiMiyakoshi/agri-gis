using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using AgriGis.Desktop.ViewModels;

namespace AgriGis.Desktop.Forms;

// F304 (Phase F WF3): 管理者向け 組織×レイヤ 権限管理ダイアログ。
// 起動経路: LayerAdminForm の「権限管理...」ボタン (admin のみ Visible)。
// 流れ:
//   1. orgs を取得 → ComboBox に並べる
//   2. orgCombo 選択変更 → GET /api/admin/organizations/{orgId}/layer-permissions
//   3. permGrid に表示、ユーザが checkBox トグル
//   4. 保存ボタン → PUT で fn_org_layer_perm_upsert (バルク)
//
// CHECK 制約 (can_edit ⊃ can_view) はクライアント側で先回り補正 (UX):
//   - canEdit ON にしたら canView も自動 ON
//   - canView OFF にしたら canEdit も自動 OFF
public partial class OrgPermissionsForm : Form
{
    private readonly OrgPermissionsViewModel _vm;
    private bool _suppressCellChange;

    public OrgPermissionsForm(IApiClient api)
    {
        _vm = new OrgPermissionsViewModel(api);
        InitializeComponent();

        orgCombo.SelectedIndexChanged += async (_, _) => await OnOrgSelectedAsync();
        permGrid.CellValueChanged += OnGridCellValueChanged;
        permGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // CheckBox は CommitEdit を明示しないと CellValueChanged が遅延する
            if (permGrid.IsCurrentCellDirty)
                permGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        saveButton.Click += async (_, _) => await OnSaveAsync();
        closeButton.Click += (_, _) => Close();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            SetStatus("組織一覧を読み込み中...");
            await _vm.LoadOrgsAsync(CancellationToken.None);
            orgCombo.Items.Clear();
            foreach (var o in _vm.Orgs)
            {
                orgCombo.Items.Add($"{o.Id}: {o.Name} ({o.Code})");
            }
            if (orgCombo.Items.Count > 0)
            {
                orgCombo.SelectedIndex = 0;
                // SelectedIndexChanged が走って OnOrgSelectedAsync 連鎖
            }
            else
            {
                SetStatus("組織が登録されていません");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"組織一覧取得エラー: {ex.Message}");
        }
    }

    private async Task OnOrgSelectedAsync()
    {
        if (orgCombo.SelectedIndex < 0 || orgCombo.SelectedIndex >= _vm.Orgs.Count) return;
        var org = _vm.Orgs[orgCombo.SelectedIndex];
        try
        {
            SetStatus($"組織 {org.Name} の権限を読み込み中...");
            await _vm.LoadPermissionsAsync(org.Id, CancellationToken.None);
            RebuildGrid();
            SetStatus($"組織 {org.Name}: {_vm.Permissions.Count} レイヤ");
        }
        catch (Exception ex)
        {
            SetStatus($"権限取得エラー: {ex.Message}");
        }
    }

    // F304: ViewModel の現在状態を grid に流し込む。一旦全 row を作り直す (シンプル + 件数小)。
    private void RebuildGrid()
    {
        _suppressCellChange = true;
        try
        {
            permGrid.Rows.Clear();
            foreach (var p in _vm.Permissions)
            {
                permGrid.Rows.Add(p.LayerId, p.LayerName, p.LayerType, p.CanView, p.CanEdit);
            }
        }
        finally
        {
            _suppressCellChange = false;
        }
    }

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressCellChange) return;
        if (e.RowIndex < 0 || e.RowIndex >= permGrid.Rows.Count) return;

        var row = permGrid.Rows[e.RowIndex];
        if (row.Cells["layerId"].Value is not int layerId) return;

        if (e.ColumnIndex == permGrid.Columns["canView"].Index)
        {
            var v = Convert.ToBoolean(row.Cells["canView"].Value);
            _vm.SetCanView(layerId, v);
            // VM 側で canEdit が auto-OFF されている可能性 → grid 側に反映
            ApplyVmToRow(row, layerId);
        }
        else if (e.ColumnIndex == permGrid.Columns["canEdit"].Index)
        {
            var v = Convert.ToBoolean(row.Cells["canEdit"].Value);
            _vm.SetCanEdit(layerId, v);
            // VM 側で canView が auto-ON されている可能性 → grid 側に反映
            ApplyVmToRow(row, layerId);
        }
    }

    // ViewModel の現在値を該当行に書き戻す (CHECK 制約の auto-flip 反映用)
    private void ApplyVmToRow(DataGridViewRow row, int layerId)
    {
        var p = _vm.GetPermission(layerId);
        if (p is null) return;
        _suppressCellChange = true;
        try
        {
            row.Cells["canView"].Value = p.CanView;
            row.Cells["canEdit"].Value = p.CanEdit;
        }
        finally
        {
            _suppressCellChange = false;
        }
    }

    private async Task OnSaveAsync()
    {
        try
        {
            SetStatus("保存中...");
            saveButton.Enabled = false;
            await _vm.SaveAsync(CancellationToken.None);
            RebuildGrid();
            SetStatus("保存完了");
        }
        catch (Exception ex)
        {
            SetStatus($"保存失敗: {ex.Message}");
        }
        finally
        {
            saveButton.Enabled = true;
        }
    }

    private void SetStatus(string text) => statusLabel.Text = text;
}
