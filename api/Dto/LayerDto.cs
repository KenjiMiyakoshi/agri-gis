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
    int StyleVersion
);
