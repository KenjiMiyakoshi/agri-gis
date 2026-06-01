using System.Text.Json;

namespace AgriGis.Desktop.Dto;

public sealed record CreateFeatureRequestDto(
    int LayerId,
    JsonElement Geometry,
    Dictionary<string, JsonElement> Attributes
);

public sealed record UpdateFeatureRequestDto(
    JsonElement? Geometry,
    Dictionary<string, JsonElement>? Attributes
);

public sealed record CreateFeatureResultDto(
    long FeatureId,
    string EntityId,
    int Version,
    int AttributesSchemaVersion
);

public sealed record PatchFeatureResultDto(
    string EntityId,
    int Version
);
