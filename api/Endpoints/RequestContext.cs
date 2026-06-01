using AgriGis.Api.Errors;
using AgriGis.Api.Middleware;

namespace AgriGis.Api.Endpoints;

// 各エンドポイントから actor / request id を取り出すヘルパ。
// RequireActor は書き込み系 (POST/PATCH/DELETE/PUT) でのみ呼ぶ。
public static class RequestContext
{
    public const string ActorHeader = "X-Actor";

    public static string RequireActor(HttpContext ctx)
    {
        var v = ctx.Request.Headers[ActorHeader].ToString().Trim();
        if (string.IsNullOrEmpty(v))
        {
            throw new MissingActorException();
        }
        return v;
    }

    public static string GetRequestId(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(RequestContextMiddleware.RequestIdItemKey, out var v) && v is string s)
        {
            return s;
        }
        // ミドルウェアが何らかの理由で先行していない場合のフォールバック
        return Guid.NewGuid().ToString("N");
    }
}

