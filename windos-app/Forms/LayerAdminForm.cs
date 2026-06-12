using System.ComponentModel;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGis.Desktop.Forms;

// WB4 B406: 管理者向けレイヤ一覧 + 編集 + 削除 + インポート起動。
// MainForm から ShowDialog で開く。サーバ側で RequireRole("admin") なので 2 重防御。
public partial class LayerAdminForm : Form
{
    private readonly IApiClient _api;
    private readonly IServiceProvider _sp;
    private BindingList<LayerAdminDto> _layers = new();

    public LayerAdminForm(IApiClient api, IServiceProvider sp)
    {
        _api = api;
        _sp = sp;
        InitializeComponent();
        importButton.Click += async (_, _) => await ImportAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        refreshButton.Click += async (_, _) => await LoadAsync();
        themeEditButton.Click += (_, _) => OpenThemeEditor();
        // F305 (Phase F WF3): 権限管理ダイアログを開く (admin 限定、本ボタンは admin Visible 制御)
        permButton.Click += (_, _) => OpenPermissionsEditor();
        closeButton.Click += (_, _) => Close();
    }

    // F305 (Phase F WF3): admin 以外は権限管理ボタンを非表示。
    // サーバ側でも RequireRole("admin") で 2 重防御。
    public void SetAdminVisibility(bool isAdmin)
    {
        permButton.Visible = isAdmin;
    }

    private void OpenPermissionsEditor()
    {
        using var form = _sp.GetRequiredService<OrgPermissionsForm>();
        form.ShowDialog(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            statusLabel.Text = "Loading...";
            var list = await _api.ListLayersAdminAsync(includeDeleted: false, asOf: null, CancellationToken.None);
            _layers = new BindingList<LayerAdminDto>(list.ToList());
            grid.DataSource = _layers;
            statusLabel.Text = $"{_layers.Count} layers";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"load failed: {ex.Message}";
        }
    }

    private async Task ImportAsync()
    {
        using var wizard = _sp.GetRequiredService<ImportWizardForm>();
        if (wizard.ShowDialog(this) == DialogResult.OK)
        {
            await LoadAsync();
        }
    }

    // D'206 (WD'2): WebGIS admin-style.html を既定ブラウザで開く
    private void OpenThemeEditor()
    {
        if (grid.CurrentRow?.DataBoundItem is not LayerAdminDto layer)
        {
            MessageBox.Show("レイヤを選択してください", "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var url = $"http://localhost:5173/admin-style.html?layerId={layer.LayerId}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ブラウザ起動失敗: {ex.Message}", "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (grid.CurrentRow?.DataBoundItem is not LayerAdminDto layer) return;
        var ok = MessageBox.Show(
            $"レイヤ '{layer.LayerName}' (id={layer.LayerId}) を論理削除しますか?",
            "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;
        try
        {
            await _api.DeleteLayerAsync(layer.LayerId, CancellationToken.None);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"削除失敗: {ex.Message}", "AgriGis", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
