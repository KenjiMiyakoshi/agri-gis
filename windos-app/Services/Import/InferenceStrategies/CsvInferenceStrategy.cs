using System.Text.Json;

namespace AgriGis.Desktop.Services.Import.InferenceStrategies;

// WB4 B403: CSV header + 100 行サンプリングから型推論。
// 試行順: boolean → date → integer → number → string
// lon/lat 列は geometry に充てるため inferred fields からは除外。
public sealed class CsvInferenceStrategy : IInferenceStrategy
{
    public string SourceFormat => "csv";

    public static IReadOnlyList<InferredField> Infer(
        string[] headers,
        IReadOnlyList<string[]> sampleRows,
        int lonColIndex,
        int latColIndex)
    {
        var result = new List<InferredField>(headers.Length);
        for (int col = 0; col < headers.Length; col++)
        {
            if (col == lonColIndex || col == latColIndex) continue;

            bool anyValue = false;
            bool anyEmpty = false;
            bool anyBoolean = false;
            bool anyInteger = false;
            bool anyNumber = false;
            bool anyDate = false;
            bool anyString = false;
            var samples = new List<JsonElement>();

            foreach (var row in sampleRows)
            {
                if (col >= row.Length) { anyEmpty = true; continue; }
                var v = row[col];
                if (string.IsNullOrEmpty(v)) { anyEmpty = true; continue; }
                anyValue = true;
                if (samples.Count < 5)
                {
                    using var d = JsonDocument.Parse(JsonSerializer.Serialize(v));
                    samples.Add(d.RootElement.Clone());
                }

                if (TypeInference.LooksLikeBoolean(v)) { anyBoolean = true; }
                else if (TypeInference.LooksLikeDate(v)) { anyDate = true; }
                else if (TypeInference.LooksLikeInteger(v)) { anyInteger = true; }
                else if (TypeInference.LooksLikeNumber(v)) { anyNumber = true; }
                else { anyString = true; }
            }

            string type;
            if (anyString) type = "string";
            else if (anyBoolean && !anyInteger && !anyNumber && !anyDate) type = "boolean";
            else if (anyDate && !anyInteger && !anyNumber && !anyBoolean) type = "date";
            else if (anyInteger && !anyNumber && !anyBoolean && !anyDate) type = "integer";
            else if ((anyInteger || anyNumber) && !anyBoolean && !anyDate) type = "number";
            else type = "string";

            var field = new InferredField
            {
                Name = headers[col],
                Type = type,
                Nullable = anyEmpty || !anyValue,
                Required = !(anyEmpty || !anyValue)
            };
            field.SampleValues.AddRange(samples);
            result.Add(field);
        }
        return result;
    }
}
