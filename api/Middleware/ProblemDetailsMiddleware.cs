using AgriGis.Api.Dto;
using AgriGis.Api.Endpoints;
using AgriGis.Api.Errors;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace AgriGis.Api.Middleware;

// 未捕捉例外を ProblemDetails (application/problem+json) として返す。
// extensions.requestId を必ず付与、ValidationException 時のみ extensions.errors を付与。
public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _log;

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> log)
    {
        _next = next;
        _log  = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            if (ctx.Response.HasStarted)
            {
                _log.LogWarning(ex, "Cannot write ProblemDetails: response already started");
                throw;
            }

            var (status, title, errors) = Map(ex);
            var pd = new ProblemDetails
            {
                Status = status,
                Title  = title,
                Type   = $"https://httpstatuses.io/{status}",
                Detail = (status >= 500) ? null : ex.Message
            };

            pd.Extensions["requestId"] = RequestContext.GetRequestId(ctx);
            if (errors is { Count: > 0 })
            {
                pd.Extensions["errors"] = errors;
            }

            if (status >= 500)
            {
                _log.LogError(ex, "Unhandled exception");
            }

            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsJsonAsync(pd);
        }
    }

    private static (int Status, string Title, IReadOnlyList<AttributeErrorDto>? Errors) Map(Exception ex) => ex switch
    {
        ValidationException v        => (422, "Validation failed", v.Errors),
        NotFoundException n          => (404, n.Message, null),
        VersionConflictException     => (409, "Version conflict", null),
        BadHttpRequestException b    => (b.StatusCode == 0 ? 400 : b.StatusCode, b.Message, null),

        // PostgreSQL SqlState からのマッピング
        PostgresException pg when pg.SqlState == "40001" => (409, "Version conflict", null),  // serialization_failure
        PostgresException pg when pg.SqlState == "02000" => (404, "Not found", null),         // no_data
        PostgresException pg when pg.SqlState == "22023" => (400, pg.MessageText, null),      // invalid_parameter_value
        PostgresException pg when pg.SqlState == "23503" => (404, pg.MessageText, null),      // foreign_key_violation
        PostgresException pg when pg.SqlState == "23505" => (409, pg.MessageText, null),      // unique_violation

        _                            => (500, "Internal Server Error", null)
    };
}
