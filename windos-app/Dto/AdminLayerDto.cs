using System.Text.Json;

namespace AgriGis.Desktop.Dto;

// WB3 B401: API の AdminLayer 系 DTO の WinForms ミラー。
// 既存 LayerSchemaDto / SchemaFieldDto (LayerDto.cs) を再利用する。
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
    DateTimeOffset UpdatedAt);
// E'102 (WE'1): DeletedAt 列 DROP。論理削除判定は valid_to <> '9999-12-31'。
// 履歴情報は layer_history.archived_at で代替。

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

public sealed record BulkFeatureItemDto(
    JsonElement Geometry,
    Dictionary<string, JsonElement>? Properties);

public sealed record BulkFeaturesRequestDto(
    Guid JobId,
    int ChunkOrdinal,
    int ChunkTotal,
    string SourceFormat,
    IReadOnlyList<BulkFeatureItemDto> Features);

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
