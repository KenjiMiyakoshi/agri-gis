using System.Text.Json;

namespace AgriGis.Api.Dto;

// null フィールドは「据え置き」を表す。Geometry/Attributes どちらも or 両方を渡せる。
public sealed record UpdateFeatureRequestDto(
    JsonElement? Geometry,
    Dictionary<string, JsonElement>? Attributes
);
