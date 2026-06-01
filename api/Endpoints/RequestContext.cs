using AgriGis.Api.Middleware;

namespace AgriGis.Api.Endpoints;

// X-Request-Id ヘルパ。X-Actor は WA3/A204 で廃止 (JWT claims に移行)、
// ICurrentUser DI 経由でアクター情報を取得する。
public static class RequestContext
{
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
