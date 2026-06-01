using System.Text.Json;

namespace AgriGis.Desktop.Core;

// API 側 0210 の AttributeValidator と挙動を合わせる：
// required と簡易型 (string/number/integer/boolean/date) のチェックのみ。
public static class AttributeValidator
{
    public static IReadOnlyList<AttributeError> Validate(
        LayerSchema schema,
        IReadOnlyDictionary<string, JsonElement> attrs)
    {
        var errors = new List<AttributeError>();
        if (schema?.Fields is null)
        {
            return errors;
        }

        foreach (var f in schema.Fields)
        {
            var has = attrs.TryGetValue(f.Key, out var val);

            if (f.Required && (!has || val.ValueKind == JsonValueKind.Null))
            {
                errors.Add(new AttributeError(f.Key, "required", $"{f.Key} is required"));
                continue;
            }

            if (!has || val.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var typeOk = f.Type switch
            {
                "string"  => val.ValueKind == JsonValueKind.String,
                "number"  => val.ValueKind == JsonValueKind.Number,
                "integer" => val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out _),
                "boolean" => val.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "date"    => val.ValueKind == JsonValueKind.String,
                _         => true
            };

            if (!typeOk)
            {
                errors.Add(new AttributeError(f.Key, "type_mismatch", $"{f.Key} expects {f.Type}"));
            }
        }
        return errors;
    }
}
