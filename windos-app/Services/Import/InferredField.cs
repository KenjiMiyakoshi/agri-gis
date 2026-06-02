using System.Text.Json;

namespace AgriGis.Desktop.Services.Import;

// WB4 B403: スキーマ推論で出力される 1 フィールド情報。
// API 送信時は SchemaFieldDto に縮退する (sampleValues / nullable / defaultValue は WinForms 内部のみ)。
public sealed class InferredField
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";       // "string" | "integer" | "number" | "boolean" | "date"
    public bool Required { get; set; }
    public bool Nullable { get; set; }
    public string? DefaultValue { get; set; }
    public List<JsonElement> SampleValues { get; } = new();
}
