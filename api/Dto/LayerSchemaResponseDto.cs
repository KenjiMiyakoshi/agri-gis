namespace AgriGis.Api.Dto;

public sealed record LayerSchemaResponseDto(
    int LayerId,
    int SchemaVersion,
    LayerSchemaDto Schema
);
