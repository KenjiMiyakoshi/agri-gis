namespace AgriGis.Api.Dto;

public sealed record LayerSchemaDto(IReadOnlyList<SchemaFieldDto> Fields);

public sealed record SchemaFieldDto(string Key, string Type, bool Required, string? Label);

// 0207: PUT /api/admin/layers/{id}/schema のリクエスト形
public sealed record UpdateSchemaRequestDto(LayerSchemaDto Schema);
