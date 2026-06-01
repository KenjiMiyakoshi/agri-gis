using System.Text.Json;

namespace AgriGis.Desktop.Dto;

public sealed record FeatureDto(
    string Type,
    JsonElement Geometry,
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

public sealed record FeatureCollectionDto(
    string Type,
    CrsDto Crs,
    IReadOnlyList<FeatureDto> Features
);

public sealed record CrsDto(string Type, CrsPropertiesDto Properties);

public sealed record CrsPropertiesDto(string Name);
