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
    bool CanEdit,
    // LG105 (Phase LG WLG1): レイヤグループ所属 (null = ルート直下) + 同一親内の表示順。
    // additive 変更。asOf の layer_history 行はグループ非対象 (presentation metadata) のため null / 0。
    int? GroupId,
    int SortOrder
);
