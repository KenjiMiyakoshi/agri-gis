namespace AgriGis.Api.Middleware;

// X-Request-Id をヘッダから取り出し、無ければ採番。
// HttpContext.Items["RequestId"] に保存し、レスポンスヘッダにも常に書き出す。
public sealed class RequestContextMiddleware
{
    public const string RequestIdItemKey = "RequestId";
    public const string RequestIdHeader = "X-Request-Id";

    private readonly RequestDelegate _next;

    public RequestContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var rid = ctx.Request.Headers[RequestIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(rid))
        {
            rid = Guid.NewGuid().ToString("N");
        }

        ctx.Items[RequestIdItemKey] = rid;
        ctx.Response.Headers[RequestIdHeader] = rid;

        await _next(ctx);
    }
}
