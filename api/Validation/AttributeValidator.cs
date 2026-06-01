using System.Text.Json;
using AgriGis.Api.Dto;

namespace AgriGis.Api.Validation;

// 属性スキーマに対する受信 attributes の最低限のバリデーション。
// 必須欠落と簡易型不一致のみチェック。regex / range / enum は本サイクル外。
public static class AttributeValidator
{
    public static IReadOnlyList<AttributeErrorDto> Validate(
        LayerSchemaDto schema,
        IReadOnlyDictionary<string, JsonElement> attrs)
    {
        var errors = new List<AttributeErrorDto>();
        if (schema?.Fields is null)
        {
            return errors;
        }

        foreach (var f in schema.Fields)
        {
            var has = attrs.TryGetValue(f.Key, out var val);

            if (f.Required && (!has || val.ValueKind == JsonValueKind.Null))
            {
                errors.Add(new AttributeErrorDto(f.Key, "required", $"{f.Key} is required"));
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
                errors.Add(new AttributeErrorDto(f.Key, "type_mismatch", $"{f.Key} expects {f.Type}"));
            }
        }
        return errors;
    }
}
