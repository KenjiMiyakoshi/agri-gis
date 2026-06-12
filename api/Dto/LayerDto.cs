namespace AgriGis.Api.Dto;

public sealed record LayerDto(
    int LayerId,
    string LayerName,
    string LayerType,
    int? OwnerOrgId,
    bool IsShared,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    LayerSchemaDto Schema,
    int StyleVersion,
    // F201 (Phase F WF2): 組織×レイヤ権限の can_edit を返す。
    // admin role 持ちは常に true。一般 user は org_layer_permission.can_edit を反映。
    bool CanEdit
);
