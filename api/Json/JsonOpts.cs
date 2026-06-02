using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgriGis.Api.Json;

// JSON シリアライズ/デシリアライズ用の標準オプション。
// Program.cs ConfigureHttpJsonOptions と同じ設定で、全 endpoint / 手動 Deserialize で共用する。
// WB2 B201 (Review② H2): FeatureEndpoints / AdminEndpoints の重複定義を撤去し本クラスに集約。
public static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
