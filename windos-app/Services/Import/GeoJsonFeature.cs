using System.Text.Json;

namespace AgriGis.Desktop.Services.Import;

// WB4 B402: ILayerSource が yield する 1 feature の最小表現。
// geometry は EPSG:4326 GeoJSON object、properties は string キー → JsonElement の dict。
public sealed record GeoJsonFeature(
    JsonElement Geometry,
    Dictionary<string, JsonElement> Properties);
