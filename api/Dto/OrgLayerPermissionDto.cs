namespace AgriGis.Api.Dto;

// F203 (Phase F WF2): 組織×レイヤ権限管理 endpoint の DTO 群。
//
// GET /api/admin/organizations/{orgId}/layer-permissions
//   → OrgLayerPermissionDto[] (全 active layer × 該当 org の (can_view, can_edit))
//
// PUT /api/admin/organizations/{orgId}/layer-permissions
//   ← OrgLayerPermsUpsertDto { permissions: OrgLayerPermItemDto[] }
//   → 200 OK + 更新後の OrgLayerPermissionDto[]

public sealed record OrgLayerPermissionDto(
    int OrgId,
    int LayerId,
    string LayerName,
    string LayerType,
    bool CanView,
    bool CanEdit);

public sealed record OrgLayerPermsUpsertDto(
    IReadOnlyList<OrgLayerPermItemDto> Permissions);

public sealed record OrgLayerPermItemDto(
    int LayerId,
    bool CanView,
    bool CanEdit);
