using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgriGis.Api;

// JSONB 列の文字列 → DTO への手動 Deserialize / 手動 Serialize 用。
// Program.cs の ConfigureHttpJsonOptions と同じ設定を使う。
internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
