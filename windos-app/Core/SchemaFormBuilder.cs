namespace AgriGis.Desktop.Core;

public enum FieldKind { String, Number, Integer, Boolean, Date, Unknown }

public sealed record FieldDescriptor(string Key, string Label, FieldKind Kind, bool Required);

// schema を見て UI 生成用の中間表現を返す。
// **Control は作らない**。Forms 側で FieldDescriptor を見てコントロールを生成する。
public static class SchemaFormBuilder
{
    public static IReadOnlyList<FieldDescriptor> Build(LayerSchema schema)
    {
        if (schema?.Fields is null)
        {
            return Array.Empty<FieldDescriptor>();
        }

        return schema.Fields.Select(f => new FieldDescriptor(
            f.Key,
            string.IsNullOrWhiteSpace(f.Label) ? f.Key : f.Label!,
            f.Type switch
            {
                "string"  => FieldKind.String,
                "number"  => FieldKind.Number,
                "integer" => FieldKind.Integer,
                "boolean" => FieldKind.Boolean,
                "date"    => FieldKind.Date,
                _         => FieldKind.Unknown
            },
            f.Required
        )).ToArray();
    }
}
