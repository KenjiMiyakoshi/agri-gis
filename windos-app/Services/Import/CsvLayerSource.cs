using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgriGis.Desktop.Services.Import;

// WB4 B402: CSV (UTF-8, ヘッダ 1 行) を Point レイヤとして読む ILayerSource。
// lat / lng 列のインデックスを ctor で指定 (Step1 で自動推測 → ユーザ確定)。
// source srid は ctor 指定 (なければ 4326 想定)、SridConverter で 4326 に正規化して emit。
public sealed class CsvLayerSource : ILayerSource
{
    private readonly string _path;
    private readonly int _lonColIndex;
    private readonly int _latColIndex;
    private readonly int _sourceSrid;
    private readonly SridConverter _converter;

    public CsvLayerSource(string path, int lonColIndex, int latColIndex, int sourceSrid)
    {
        _path = path;
        _lonColIndex = lonColIndex;
        _latColIndex = latColIndex;
        _sourceSrid = sourceSrid;
        _converter = new SridConverter();
    }

    public string SourceFormat => "csv";
    public int? SourceSrid => _sourceSrid;

    public async Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct)
    {
        using var sr = new StreamReader(_path);
        var headerLine = await sr.ReadLineAsync(ct);
        if (headerLine is null) return Array.Empty<InferredField>();
        var headers = ParseCsvLine(headerLine);

        // 100 行サンプリング
        var samples = new List<string[]>(100);
        for (int i = 0; i < 100; i++)
        {
            var line = await sr.ReadLineAsync(ct);
            if (line is null) break;
            samples.Add(ParseCsvLine(line));
        }

        return InferenceStrategies.CsvInferenceStrategy.Infer(headers, samples, _lonColIndex, _latColIndex);
    }

    public async IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(
        int targetSrid,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (targetSrid != 4326)
            throw new NotSupportedException($"CsvLayerSource: only 4326 target is supported, got {targetSrid}");

        using var sr = new StreamReader(_path);
        var headerLine = await sr.ReadLineAsync(ct);
        if (headerLine is null) yield break;
        var headers = ParseCsvLine(headerLine);

        string? line;
        while ((line = await sr.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            var cols = ParseCsvLine(line);
            if (cols.Length <= Math.Max(_lonColIndex, _latColIndex)) continue;

            if (!double.TryParse(cols[_lonColIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            if (!double.TryParse(cols[_latColIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;

            // SRID 変換 (4326 へ)
            var (lon4326, lat4326) = _converter.To4326(_sourceSrid, lon, lat);

            var geomJson = $"{{\"type\":\"Point\",\"coordinates\":[{lon4326.ToString("R", CultureInfo.InvariantCulture)},{lat4326.ToString("R", CultureInfo.InvariantCulture)}]}}";
            using var geomDoc = JsonDocument.Parse(geomJson);
            var geometry = geomDoc.RootElement.Clone();

            var props = new Dictionary<string, JsonElement>();
            for (int i = 0; i < headers.Length && i < cols.Length; i++)
            {
                if (i == _lonColIndex || i == _latColIndex) continue;
                using var pd = JsonDocument.Parse(JsonSerializer.Serialize(cols[i]));
                props[headers[i]] = pd.RootElement.Clone();
            }
            yield return new GeoJsonFeature(geometry, props);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // 簡易 CSV パーサ: ダブルクォート対応、エスケープは "" のみ。複雑なケースは Phase C で考える。
    internal static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else { inQuotes = false; }
                }
                else { sb.Append(c); }
            }
            else
            {
                if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') { inQuotes = true; }
                else { sb.Append(c); }
            }
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
