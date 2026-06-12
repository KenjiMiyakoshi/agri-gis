namespace AgriGis.Desktop.Dto;

// F306 (Phase F WF3): API record と命名一致 (camelCase JSON は JsonSerializerOptions で対応)
// 組織×レイヤ権限管理 endpoint のレスポンス/リクエスト用

public sealed record OrgLayerPermissionDto(
    int OrgId,
    int LayerId,
    string LayerName,
    string LayerType,
    bool CanView,
    bool CanEdit
);

public sealed record OrgLayerPermsUpsertDto(
    IReadOnlyList<OrgLayerPermItemDto> Permissions
);

public sealed record OrgLayerPermItemDto(
    int LayerId,
    bool CanView,
    bool CanEdit
);

// GET /api/admin/organizations 用 (F306 で新規)
public sealed record OrgDto(
    int Id,
    string Name,
    string Code,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
