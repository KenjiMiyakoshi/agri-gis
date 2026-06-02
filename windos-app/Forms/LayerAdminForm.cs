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
        closeButton.Click += (_, _) => Close();
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
            var list = await _api.ListLayersAdminAsync(includeDeleted: false, CancellationToken.None);
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
