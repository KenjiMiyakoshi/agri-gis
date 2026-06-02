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
    /// </summary>
    public static string Build(string themeName, JsonElement themeJson)
    {
        // 簡略 SLD (1 PolygonSymbolizer のみ)
        var fillColor = TryGetString(themeJson, "fillColor") ?? "#4CAF50";
        var fillOpacity = TryGetDouble(themeJson, "fillOpacity") ?? 0.5;
        var strokeColor = TryGetString(themeJson, "strokeColor") ?? "#1B5E20";
        var strokeWidth = TryGetDouble(themeJson, "strokeWidth") ?? 1.0;

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
        sb.AppendLine($"      </FeatureTypeStyle>");
        sb.AppendLine($"    </UserStyle>");
        sb.AppendLine($"  </NamedLayer>");
        sb.AppendLine("</StyledLayerDescriptor>");
        return sb.ToString();
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
