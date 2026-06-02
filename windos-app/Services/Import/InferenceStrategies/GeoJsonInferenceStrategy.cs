using System.Text.Json;

namespace AgriGis.Desktop.Services.Import.InferenceStrategies;

// WB4 B403: GeoJSON FeatureCollection の properties から型推論する純粋関数。
// 最初の 100 feature の properties キーを収集し、各キーの ValueKind から型を決める。
// - 全て integer なら "integer"、混在 (整数+小数) なら "number"
// - 全て boolean なら "boolean"
// - 全て date 形式 string なら "date"、文字混在なら "string"
// - null 検出で nullable=true
public sealed class GeoJsonInferenceStrategy : IInferenceStrategy
{
    public string SourceFormat => "geojson";

    public static IReadOnlyList<InferredField> Infer(JsonElement featuresArray)
    {
        if (featuresArray.ValueKind != JsonValueKind.Array)
            return Array.Empty<InferredField>();

        // 列順序を保持しつつ収集 (最大 100 feature をスキャン)
        var byName = new Dictionary<string, ColumnAccum>(StringComparer.Ordinal);
        var order = new List<string>();
        int count = 0;

        foreach (var feat in featuresArray.EnumerateArray())
        {
            if (count >= 100) break;
            if (!feat.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                continue;
            count++;

            foreach (var p in props.EnumerateObject())
            {
                if (!byName.TryGetValue(p.Name, out var col))
                {
                    col = new ColumnAccum();
                    byName[p.Name] = col;
                    order.Add(p.Name);
                }
                col.Observe(p.Value);
            }
        }

        var result = new List<InferredField>(order.Count);
        foreach (var name in order)
        {
            var col = byName[name];
            var field = new InferredField
            {
                Name = name,
                Type = col.ResolveType(),
                Nullable = col.Nullable,
                Required = !col.Nullable
            };
            field.SampleValues.AddRange(col.Samples);
            result.Add(field);
        }
        return result;
    }

    private sealed class ColumnAccum
    {
        public bool Nullable;
        public bool AnyString;
        public bool AnyNonInteger;     // number で整数でないものを見たか
        public bool AnyBoolean;
        public bool AnyInteger;
        public bool AnyNumber;
        public bool AnyDate;
        public List<JsonElement> Samples { get; } = new();

        public void Observe(JsonElement e)
        {
            if (Samples.Count < 5) Samples.Add(e.Clone());

            switch (e.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    Nullable = true; break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    AnyBoolean = true; break;
                case JsonValueKind.Number:
                    if (e.TryGetInt64(out _)) AnyInteger = true;
                    else { AnyNumber = true; AnyNonInteger = true; }
                    break;
                case JsonValueKind.String:
                    var s = e.GetString();
                    if (string.IsNullOrEmpty(s)) Nullable = true;
                    else if (TypeInference.LooksLikeDate(s)) AnyDate = true;
                    else AnyString = true;
                    break;
                default:
                    AnyString = true; break;
            }
        }

        public string ResolveType()
        {
            // 単一型に確定するパターン
            if (AnyString) return "string";
            if (AnyBoolean && !AnyInteger && !AnyNumber && !AnyDate) return "boolean";
            if (AnyDate && !AnyInteger && !AnyNumber && !AnyBoolean) return "date";
            if (AnyInteger && !AnyNonInteger && !AnyBoolean && !AnyDate) return "integer";
            if ((AnyInteger || AnyNumber) && !AnyBoolean && !AnyDate) return "number";
            return "string"; // 混在は string に丸める
        }
    }
}
