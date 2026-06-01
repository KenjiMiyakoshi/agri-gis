namespace AgriGis.Desktop.Core;

public sealed record SchemaField(string Key, string Type, bool Required, string? Label);

public sealed record LayerSchema(IReadOnlyList<SchemaField> Fields);
