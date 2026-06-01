namespace AgriGis.Api.Dto;

public sealed record FeatureCollectionDto(
    string Type,                 // "FeatureCollection" 固定
    CrsDto Crs,
    IReadOnlyList<FeatureDto> Features
);

public sealed record CrsDto(string Type, CrsPropertiesDto Properties);

public sealed record CrsPropertiesDto(string Name);
