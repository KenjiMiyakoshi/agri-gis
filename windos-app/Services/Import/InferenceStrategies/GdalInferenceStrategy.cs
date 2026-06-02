using System.Text.Json;
using OSGeo.OGR;

namespace AgriGis.Desktop.Services.Import.InferenceStrategies;

// WC2 C301: OGR FieldDefn を InferredField に写像する純粋関数。
//
// OFT 型表 (PHASE_C_DESIGN_P §6.7):
//   OFTInteger / OFTInteger64    → "integer"
//   OFTReal                       → "number"
//   OFTString                     → "string" (100 件全 ISO8601 で "date" 昇格)
//   OFTDate / OFTDateTime         → "date"
//   OFTBinary                     → skip + WARN
//   OFTStringList / OFTIntegerList / OFTRealList → "string" (JSON 配列で文字列化)
//
// nullable 再推定: 100 サンプリングで 1 件でも null/空文字なら nullable=true。
// date 昇格: OFTString で 100 件全てが ISO8601 (yyyy-MM-dd) なら "date" に昇格。
public sealed class GdalInferenceStrategy : IInferenceStrategy
{
    public string SourceFormat => "shapefile";

    public const int SampleSize = 100;

    /// <summary>
    /// OGR Layer から InferredField のリストを生成する。Layer は呼び出し側でリセットすること。
    /// </summary>
    public static IReadOnlyList<InferredField> Infer(Layer layer, List<string>? warnings = null)
    {
        warnings ??= new();
        var defn = layer.GetLayerDefn();
        var fieldCount = defn.GetFieldCount();

        var fields = new List<InferredField>(fieldCount);
        var fieldDefns = new List<FieldDefn>(fieldCount);
        for (int i = 0; i < fieldCount; i++)
        {
            var fd = defn.GetFieldDefn(i);
            var type = fd.GetFieldType();
            if (type == FieldType.OFTBinary)
            {
                warnings.Add($"OFTBinary field skipped: {fd.GetName()}");
                continue;
            }

            var field = new InferredField
            {
                Name = fd.GetName(),
                Type = MapOftToString(type),
                Required = true,   // 後段で nullable 確定後に Required = !Nullable に揃える
                Nullable = false
            };
            fields.Add(field);
            fieldDefns.Add(fd);
        }

        // サンプリング: 100 feature
        layer.ResetReading();
        int sampled = 0;
        Feature? feat;
        var sampleValues = new List<string?>[fields.Count];
        for (int i = 0; i < sampleValues.Length; i++) sampleValues[i] = new List<string?>(SampleSize);

        while (sampled < SampleSize && (feat = layer.GetNextFeature()) != null)
        {
            using (feat)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    var idx = defn.GetFieldIndex(fields[i].Name);
                    if (idx < 0) { sampleValues[i].Add(null); continue; }
                    var isNullField = !feat.IsFieldSet(idx) || feat.IsFieldNull(idx);
                    string? v = isNullField ? null : feat.GetFieldAsString(idx);
                    sampleValues[i].Add(v);
                }
                sampled++;
            }
        }
        layer.ResetReading();

        // nullable + date 昇格を確定
        for (int i = 0; i < fields.Count; i++)
        {
            var samples = sampleValues[i];
            bool anyNull = samples.Any(v => string.IsNullOrEmpty(v));
            fields[i].Nullable = anyNull;
            fields[i].Required = !anyNull;

            // OFTString で非 null/空白が全て ISO8601 なら "date" 昇格
            if (fields[i].Type == "string")
            {
                var nonEmpty = samples.Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (nonEmpty.Count > 0 && nonEmpty.All(v => TypeInference.LooksLikeDate(v!)))
                {
                    fields[i].Type = "date";
                }
            }

            // sample values をフィールドにも保持 (Phase B InferredField.SampleValues は List<JsonElement>)
            foreach (var v in samples.Take(5))
            {
                if (v is null) continue;
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(v));
                fields[i].SampleValues.Add(doc.RootElement.Clone());
            }
        }

        return fields;
    }

    internal static string MapOftToString(FieldType oft) => oft switch
    {
        FieldType.OFTInteger or FieldType.OFTInteger64 => "integer",
        FieldType.OFTReal => "number",
        FieldType.OFTDate or FieldType.OFTDateTime or FieldType.OFTTime => "date",
        FieldType.OFTStringList or FieldType.OFTIntegerList or FieldType.OFTInteger64List
            or FieldType.OFTRealList => "string",
        FieldType.OFTString => "string",
        _ => "string"
    };
}
