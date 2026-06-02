using System.Text.Json;

namespace AgriGis.Desktop.Services.Import.Srid;

// WC2 C104: AssumeWgs84 経路で audit_log.meta_jsonb に書く JSON を組み立てるヘルパ。
// PHASE_C_DESIGN_P §6.3 / §6.10 / 実装リスクレビュー Design 決定 2:
//   { "srid_inferred": true, "srid_fallback_policy": "AssumeWgs84" }
public static class SridMetaJsonBuilder
{
    public static string BuildAssumeWgs84MetaJson()
    {
        var obj = new Dictionary<string, object>
        {
            ["srid_inferred"] = true,
            ["srid_fallback_policy"] = "AssumeWgs84"
        };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// SridResolutionState を見て、書くべき meta_json があれば返す。null は「書かない」。
    /// </summary>
    public static string? BuildIfNeeded(SridResolutionState state)
    {
        return state == SridResolutionState.FallbackToWgs84
            ? BuildAssumeWgs84MetaJson()
            : null;
    }
}
