using System.Text.Json;

namespace AgriGis.Api.Dto;

public sealed record FeatureDto(
    string Type,                 // "Feature" 固定
    JsonElement Geometry,        // GeoJSON geometry (EPSG:4326)
    FeaturePropertiesDto Properties
);

public sealed record FeaturePropertiesDto(
    long FeatureId,
    int LayerId,
    string EntityId,
    int Version,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int AttributesSchemaVersion,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Dictionary<string, JsonElement> Attributes
);
