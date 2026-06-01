using System.Text.Json;

namespace AgriGis.Api.Dto;

public sealed record FeatureHistoryDto(
    long HistoryId,
    long FeatureId,
    int LayerId,
    string EntityId,
    JsonElement Geometry,                            // GeoJSON (EPSG:4326)
    Dictionary<string, JsonElement> Attributes,
    int AttributesSchemaVersion,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset ArchivedAt,
    string ArchivedBy,
    string ArchivedReason                            // "update" | "delete"
);
