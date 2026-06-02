namespace AgriGis.Api.Dto;

// WB2 B201: AdminLayers endpoint 用の DTO 一式。

public sealed record LayerAdminDto(
    int LayerId,
    string LayerName,
    string LayerType,
    string? GeometryType,
    string? SourceFormat,
    int? SourceSrid,
    string? Description,
    int SchemaVersion,
    LayerSchemaDto Schema,
    Guid? CreatedBy,
    int? CreatedOrgId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record CreateLayerRequestDto(
    string LayerName,
    string LayerType,
    string? GeometryType,
    string? SourceFormat,
    int? SourceSrid,
    string? Description,
    LayerSchemaDto? Schema);

public sealed record UpdateLayerRequestDto(
    string? LayerName,
    string? LayerType,
    string? GeometryType,
    string? Description);

// WB3 B203/B204 で使用するが、B201 で DTO は先行定義しておく。
public sealed record BulkFeaturesRequestDto(
    Guid JobId,
    int ChunkOrdinal,
    int ChunkTotal,
    string SourceFormat,
    IReadOnlyList<BulkFeatureItemDto> Features);

public sealed record BulkFeatureItemDto(
    System.Text.Json.JsonElement Geometry,
    Dictionary<string, System.Text.Json.JsonElement>? Properties);

public sealed record BulkFeaturesResponseDto(
    int InsertedCount,
    IReadOnlyList<long> FeatureIds);

public sealed record ImportJobDto(
    Guid JobId,
    int LayerId,
    string Status,
    int? TotalCount,
    int InsertedCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorText);

public sealed record StartImportJobRequestDto(int? TotalCount);

public sealed record FinalizeImportJobRequestDto(string Status, string? ErrorText);
