using System.Text.Json;

namespace AgriGis.Desktop.Services.Import.InferenceStrategies;

// WB4 B403: スキーマ推論のフォーマット別ストラテジ。
// 純粋関数: 入力 → 出力で副作用ゼロ、テストで [Theory] 化しやすい。
public interface IInferenceStrategy
{
    string SourceFormat { get; }
}

// 静的なヘルパ関数で型推論ロジックを共通化 (ストラテジ実装間で共有)
internal static class TypeInference
{
    // ISO8601 date 判定 (YYYY-MM-DD のみ Phase B)
    public static bool LooksLikeDate(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Length == 10 && DateOnly.TryParseExact(s, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);
    }

    public static bool LooksLikeInteger(string s)
        => long.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    public static bool LooksLikeNumber(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);

    public static bool LooksLikeBoolean(string s)
        => s.Equals("true", StringComparison.OrdinalIgnoreCase)
           || s.Equals("false", StringComparison.OrdinalIgnoreCase);

    // JsonValueKind ベースの直接マップ (GeoJSON properties 用)
    public static string FromJsonValueKind(JsonElement e)
    {
        return e.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number when e.TryGetInt64(out _) => "integer",
            JsonValueKind.Number => "number",
            JsonValueKind.String => LooksLikeDate(e.GetString() ?? "") ? "date" : "string",
            _ => "string"
        };
    }
}
