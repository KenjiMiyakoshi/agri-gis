using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgriGis.Desktop.Services.Import;

// WB4 B402: GeoJSON FeatureCollection (EPSG:4326) を読む ILayerSource。
// NetTopologySuite に依存せず、System.Text.Json で素直にパースする (Phase B はシンプル優先)。
// SourceSrid は 4326 固定 (RFC 7946 規定)。targetSrid=4326 以外は今のところサポート外。
public sealed class GeoJsonLayerSource : ILayerSource
{
    private readonly string _path;

    public GeoJsonLayerSource(string path)
    {
        _path = path;
    }

    public string SourceFormat => "geojson";
    public int? SourceSrid => 4326;

    public async Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct)
    {
        using var fs = File.OpenRead(_path);
        using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("features", out var features))
            return Array.Empty<InferredField>();

        return InferenceStrategies.GeoJsonInferenceStrategy.Infer(features);
    }

    public async IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(
        int targetSrid,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (targetSrid != 4326)
            throw new NotSupportedException($"GeoJsonLayerSource: only 4326 target is supported, got {targetSrid}");

        using var fs = File.OpenRead(_path);
        using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("features", out var features))
            yield break;

        foreach (var f in features.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            if (!f.TryGetProperty("geometry", out var geom)) continue;

            var props = new Dictionary<string, JsonElement>();
            if (f.TryGetProperty("properties", out var pe) && pe.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in pe.EnumerateObject())
                    props[p.Name] = p.Value.Clone();
            }
            yield return new GeoJsonFeature(geom.Clone(), props);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
