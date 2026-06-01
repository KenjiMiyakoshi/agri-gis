using AgriGis.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace AgriGis.Api.Auth;

// 認証失敗 (401) / 認可失敗 (403) を ProblemDetails (application/problem+json) で返す。
// 既定の AuthorizationMiddlewareResultHandler は 401/403 にボディなしで返すため、
// 既存 ProblemDetailsMiddleware の形式 (extensions.requestId 含む) に揃える。
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var (status, title) = authorizeResult.Challenged
            ? (StatusCodes.Status401Unauthorized, "Authentication required")
            : (StatusCodes.Status403Forbidden,    "Forbidden");

        var pd = new ProblemDetails
        {
            Status = status,
            Title  = title,
            Type   = $"https://httpstatuses.io/{status}",
        };
        pd.Extensions["requestId"] = RequestContext.GetRequestId(context);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(pd);
    }
}
