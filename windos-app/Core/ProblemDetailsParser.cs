using System.Text.Json;

namespace AgriGis.Desktop.Core;

// ASP.NET Core の ProblemDetails (application/problem+json) を解釈する。
// errors[] は extensions.errors と top-level errors の両方を見る
// (シリアライザの仕様で位置が変わるため吸収)。
public static class ProblemDetailsParser
{
    public sealed record ParsedProblem(
        int? Status,
        string? Title,
        string? RequestId,
        IReadOnlyList<AttributeError> Errors);

    public static ParsedProblem Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParsedProblem(null, null, null, Array.Empty<AttributeError>());
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int? status = TryGetInt(root, "status");
            string? title = TryGetString(root, "title");
            string? requestId = TryGetString(root, "requestId");
            JsonElement? errorsEl = TryGetProperty(root, "errors");

            // top-level に無ければ extensions 配下を見る
            if (root.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Object)
            {
                requestId ??= TryGetString(ext, "requestId");
                if (errorsEl is null && ext.TryGetProperty("errors", out var extErrors))
                {
                    errorsEl = extErrors;
                }
            }

            var errors = ParseErrors(errorsEl);
            return new ParsedProblem(status, title, requestId, errors);
        }
        catch (JsonException)
        {
            return new ParsedProblem(null, null, null, Array.Empty<AttributeError>());
        }
    }

    private static IReadOnlyList<AttributeError> ParseErrors(JsonElement? el)
    {
        if (el is null || el.Value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AttributeError>();
        }

        var list = new List<AttributeError>();
        foreach (var item in el.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var key = TryGetString(item, "attributeKey") ?? string.Empty;
            var code = TryGetString(item, "code") ?? string.Empty;
            var message = TryGetString(item, "message") ?? string.Empty;
            list.Add(new AttributeError(key, code, message));
        }
        return list;
    }

    private static int? TryGetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        return null;
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static JsonElement? TryGetProperty(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) ? p : null;
}
