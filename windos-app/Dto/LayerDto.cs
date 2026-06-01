namespace AgriGis.Desktop.Dto;

// API record と命名一致 (camelCase JSON は JsonSerializerOptions で対応)
public sealed record LayerDto(
    int LayerId,
    string LayerName,
    string LayerType,
    int? OwnerOrgId,
    bool IsShared,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    LayerSchemaDto Schema
);

public sealed record LayerSchemaDto(IReadOnlyList<SchemaFieldDto> Fields);

public sealed record SchemaFieldDto(string Key, string Type, bool Required, string? Label);

public sealed record LayerSchemaResponseDto(
    int LayerId,
    int SchemaVersion,
    LayerSchemaDto Schema
);
