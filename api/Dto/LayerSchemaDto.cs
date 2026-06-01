namespace AgriGis.Api.Dto;

public sealed record LayerSchemaDto(IReadOnlyList<SchemaFieldDto> Fields);

public sealed record SchemaFieldDto(string Key, string Type, bool Required, string? Label);

public sealed record UpdateSchemaRequestDto(IReadOnlyList<SchemaFieldDto> Fields);
