using System.Text;
using System.Text.Json;

namespace AgriGis.Api.Style;

// D203 (WD2): style_json (1 theme 分) を SLD XML に変換する純粋関数。
// Phase D MVP 対応プロパティ:
//   - fillColor / fillOpacity / strokeColor / strokeWidth (PolygonSymbolizer 系)
// Phase D' 拡張候補:
//   - categoryField + categories (= byOwner 流カテゴリ別カラー)
//   - PointSymbolizer / LineSymbolizer
//   - ラベル / TextSymbolizer
//   - カラーランプ
public static class SldXmlBuilder
{
    /// <summary>
    /// theme 1 つの JsonElement を SLD XML 文字列に変換する。
    /// D'205 (WD'2): colorRamp プロパティ {field, breaks, colors} があれば
    /// N 段 Rule を ogc:PropertyIsLessThan で生成 (カラーランプ自動配色)。
    /// </summary>
    public static string Build(string themeName, JsonElement themeJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<StyledLayerDescriptor version=\"1.0.0\"");
        sb.AppendLine("    xmlns=\"http://www.opengis.net/sld\"");
        sb.AppendLine("    xmlns:ogc=\"http://www.opengis.net/ogc\"");
        sb.AppendLine("    xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
        sb.AppendLine($"  <NamedLayer>");
        sb.AppendLine($"    <Name>{themeName}</Name>");
        sb.AppendLine($"    <UserStyle>");
        sb.AppendLine($"      <Title>Theme {EscapeXml(themeName)}</Title>");
        sb.AppendLine($"      <FeatureTypeStyle>");

        if (TryGetColorRamp(themeJson, out var field, out var breaks, out var colors))
        {
            AppendColorRampRules(sb, field, breaks, colors);
        }
        else
        {
            AppendSimpleRule(sb, themeJson);
        }

        sb.AppendLine($"      </FeatureTypeStyle>");
        sb.AppendLine($"    </UserStyle>");
        sb.AppendLine($"  </NamedLayer>");
        sb.AppendLine("</StyledLayerDescriptor>");
        return sb.ToString();
    }

    private static void AppendSimpleRule(StringBuilder sb, JsonElement themeJson)
    {
        var fillColor = TryGetString(themeJson, "fillColor") ?? "#4CAF50";
        var fillOpacity = TryGetDouble(themeJson, "fillOpacity") ?? 0.5;
        var strokeColor = TryGetString(themeJson, "strokeColor") ?? "#1B5E20";
        var strokeWidth = TryGetDouble(themeJson, "strokeWidth") ?? 1.0;

        sb.AppendLine($"        <Rule>");
        sb.AppendLine($"          <PolygonSymbolizer>");
        sb.AppendLine($"            <Fill>");
        sb.AppendLine($"              <CssParameter name=\"fill\">{EscapeXml(fillColor)}</CssParameter>");
        sb.AppendLine($"              <CssParameter name=\"fill-opacity\">{fillOpacity}</CssParameter>");
        sb.AppendLine($"            </Fill>");
        sb.AppendLine($"            <Stroke>");
        sb.AppendLine($"              <CssParameter name=\"stroke\">{EscapeXml(strokeColor)}</CssParameter>");
        sb.AppendLine($"              <CssParameter name=\"stroke-width\">{strokeWidth}</CssParameter>");
        sb.AppendLine($"            </Stroke>");
        sb.AppendLine($"          </PolygonSymbolizer>");
        sb.AppendLine($"        </Rule>");
    }

    // D'205 (WD'2): N 段カラーランプ Rule 生成
    // breaks = [v1, v2, v3, v4]、colors = [c0, c1, c2, c3, c4] (colors の数 = breaks の数 + 1)
    // Rule 0: x < v1 → c0
    // Rule 1: v1 <= x < v2 → c1
    // ...
    // Rule N: x >= vN → cN
    private static void AppendColorRampRules(StringBuilder sb, string field, double[] breaks, string[] colors)
    {
        var safeField = EscapeXml(field);
        for (int i = 0; i < colors.Length; i++)
        {
            double? lower = i == 0 ? null : breaks[i - 1];
            double? upper = i < breaks.Length ? breaks[i] : null;
            sb.AppendLine($"        <Rule>");
            sb.AppendLine($"          <Title>{safeField} bin {i}</Title>");
            sb.AppendLine($"          <ogc:Filter>");
            sb.AppendLine($"            <ogc:And>");
            if (lower.HasValue)
            {
                sb.AppendLine($"              <ogc:PropertyIsGreaterThanOrEqualTo>");
                sb.AppendLine($"                <ogc:PropertyName>{safeField}</ogc:PropertyName>");
                sb.AppendLine($"                <ogc:Literal>{lower.Value}</ogc:Literal>");
                sb.AppendLine($"              </ogc:PropertyIsGreaterThanOrEqualTo>");
            }
            if (upper.HasValue)
            {
                sb.AppendLine($"              <ogc:PropertyIsLessThan>");
                sb.AppendLine($"                <ogc:PropertyName>{safeField}</ogc:PropertyName>");
                sb.AppendLine($"                <ogc:Literal>{upper.Value}</ogc:Literal>");
                sb.AppendLine($"              </ogc:PropertyIsLessThan>");
            }
            if (!lower.HasValue && !upper.HasValue)
            {
                // 単一 bin の場合 (breaks.Length=0) は filter 不要、デフォルトで全件マッチ
                // ただし And タグが空になるので閉じない
            }
            sb.AppendLine($"            </ogc:And>");
            sb.AppendLine($"          </ogc:Filter>");
            sb.AppendLine($"          <PolygonSymbolizer>");
            sb.AppendLine($"            <Fill>");
            sb.AppendLine($"              <CssParameter name=\"fill\">{EscapeXml(colors[i])}</CssParameter>");
            sb.AppendLine($"              <CssParameter name=\"fill-opacity\">0.7</CssParameter>");
            sb.AppendLine($"            </Fill>");
            sb.AppendLine($"            <Stroke>");
            sb.AppendLine($"              <CssParameter name=\"stroke\">#333333</CssParameter>");
            sb.AppendLine($"              <CssParameter name=\"stroke-width\">0.5</CssParameter>");
            sb.AppendLine($"            </Stroke>");
            sb.AppendLine($"          </PolygonSymbolizer>");
            sb.AppendLine($"        </Rule>");
        }
    }

    private static bool TryGetColorRamp(JsonElement themeJson, out string field, out double[] breaks, out string[] colors)
    {
        field = "";
        breaks = Array.Empty<double>();
        colors = Array.Empty<string>();
        if (themeJson.ValueKind != JsonValueKind.Object) return false;
        if (!themeJson.TryGetProperty("colorRamp", out var cr) || cr.ValueKind != JsonValueKind.Object) return false;
        var f = TryGetString(cr, "field");
        if (string.IsNullOrEmpty(f)) return false;
        if (!cr.TryGetProperty("breaks", out var brArr) || brArr.ValueKind != JsonValueKind.Array) return false;
        if (!cr.TryGetProperty("colors", out var clArr) || clArr.ValueKind != JsonValueKind.Array) return false;
        breaks = brArr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number)
            .Select(e => e.GetDouble())
            .ToArray();
        colors = clArr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
        if (colors.Length == 0) return false;
        // colors.Length は breaks.Length + 1 が期待値だが、ずれていても N 個まで描画する
        field = f!;
        return true;
    }

    private static string? TryGetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static double? TryGetDouble(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&apos;");
}
