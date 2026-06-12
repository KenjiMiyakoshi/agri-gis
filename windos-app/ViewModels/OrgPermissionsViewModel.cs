using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.ViewModels;

// F304 (Phase F WF3): OrgPermissionsForm の ViewModel (UI 非依存)。
// 責務:
//   - 組織一覧と現在選択中の組織の (layer × can_view × can_edit) を保持
//   - CHECK 制約 (can_edit ⊃ can_view) のクライアント側強制 — UI 側は本 ViewModel が
//     提供する SetCanEdit/SetCanView を呼んで「edit ON で view も ON」を担保する
//   - 保存時に PUT 用の DTO を組み立てて IApiClient へ
//
// UI から呼ぶメソッドは全て同期 + 例外なし。LoadOrgsAsync / LoadPermissionsAsync /
// SaveAsync は IO のため async。
public sealed class OrgPermissionsViewModel
{
    private readonly IApiClient _api;
    private IReadOnlyList<OrgDto> _orgs = Array.Empty<OrgDto>();
    private readonly List<OrgLayerPermissionDto> _perms = new();
    private int _selectedOrgId = -1;

    public OrgPermissionsViewModel(IApiClient api)
    {
        _api = api;
    }

    public IReadOnlyList<OrgDto> Orgs => _orgs;
    public IReadOnlyList<OrgLayerPermissionDto> Permissions => _perms;
    public int SelectedOrgId => _selectedOrgId;

    public async Task LoadOrgsAsync(CancellationToken ct)
    {
        _orgs = await _api.ListOrgsAsync(ct);
    }

    public async Task LoadPermissionsAsync(int orgId, CancellationToken ct)
    {
        _selectedOrgId = orgId;
        var list = await _api.GetOrgLayerPermissionsAsync(orgId, ct);
        _perms.Clear();
        _perms.AddRange(list);
    }

    /// <summary>
    /// can_view を切り替える。OFF にすると can_edit も自動 OFF (CHECK 制約準拠)。
    /// 該当 layerId が無ければ no-op。
    /// </summary>
    public void SetCanView(int layerId, bool canView)
    {
        var idx = FindPermIndex(layerId);
        if (idx < 0) return;
        var p = _perms[idx];
        _perms[idx] = p with
        {
            CanView = canView,
            CanEdit = canView ? p.CanEdit : false
        };
    }

    /// <summary>
    /// can_edit を切り替える。ON にすると can_view も自動 ON (CHECK 制約準拠)。
    /// 該当 layerId が無ければ no-op。
    /// </summary>
    public void SetCanEdit(int layerId, bool canEdit)
    {
        var idx = FindPermIndex(layerId);
        if (idx < 0) return;
        var p = _perms[idx];
        _perms[idx] = p with
        {
            CanEdit = canEdit,
            CanView = canEdit ? true : p.CanView
        };
    }

    public OrgLayerPermissionDto? GetPermission(int layerId)
    {
        var idx = FindPermIndex(layerId);
        return idx >= 0 ? _perms[idx] : null;
    }

    private int FindPermIndex(int layerId)
    {
        for (int i = 0; i < _perms.Count; i++)
        {
            if (_perms[i].LayerId == layerId) return i;
        }
        return -1;
    }

    /// <summary>
    /// 現在の _perms を PUT で API に送って、レスポンス (確定後の状態) で _perms を上書き。
    /// </summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        if (_selectedOrgId < 0) throw new InvalidOperationException("No organization selected");
        var items = _perms
            .Select(p => new OrgLayerPermItemDto(p.LayerId, p.CanView, p.CanEdit))
            .ToList();
        var req = new OrgLayerPermsUpsertDto(items);
        var updated = await _api.UpdateOrgLayerPermissionsAsync(_selectedOrgId, req, ct);
        _perms.Clear();
        _perms.AddRange(updated);
    }
}
