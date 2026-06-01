using System.Text.Json;

namespace AgriGis.Api.Dto;

public sealed record CreateFeatureRequestDto(
    int LayerId,
    JsonElement Geometry,                            // GeoJSON (EPSG:4326)
    Dictionary<string, JsonElement> Attributes
);
