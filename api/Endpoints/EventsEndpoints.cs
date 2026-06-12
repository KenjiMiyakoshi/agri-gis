using System.Text;
using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace AgriGis.Api.Endpoints;

// D'301 (WD'3): Server-Sent Events で layer 無効化イベントを配信。
// GET /api/events/layers/{layerId}/stream — per-layer (deprecated F'101 で)
// GET /api/events/stream-all?layerIds=1,2,3 — multi-layer (F'101 で追加)
//
// 認証: EventSource は Authorization ヘッダを送れないため、Program.cs の
// JwtBearer.OnMessageReceived で ?access_token= からも JWT を受領する分岐を追加する。
public static class EventsEndpoints
{
    // F'101 (Phase F' WF'1): 旧 per-layer endpoint の Sunset 日付 (Phase G で物理削除予定)
    private const string SunsetDate = "Sun, 31 Dec 2026 23:59:59 GMT";

    public static RouteGroupBuilder MapEventsEndpoints(this RouteGroupBuilder group)
    {
        // F'101 (Phase F' WF'1): 旧 per-layer endpoint は deprecated。Sunset ヘッダ付与。
        group.MapGet("/layers/{layerId:int}/stream", async (
            int layerId,
            HttpContext httpContext,
            ILayerInvalidationBroker broker,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            httpContext.Response.Headers.Append("Cache-Control", "no-cache, no-store");
            httpContext.Response.Headers.Append("Connection", "keep-alive");
            httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
            // F'101: deprecated 注記
            httpContext.Response.Headers.Append("Sunset", SunsetDate);
            httpContext.Response.Headers.Append("Link",
                "</api/events/stream-all>; rel=\"successor-version\"");
            httpContext.Response.Headers.Append("Deprecation", "true");

            // 直近 5 秒の event を replay
            foreach (var ev in broker.ReplayRecent(layerId, TimeSpan.FromSeconds(5)))
            {
                await WriteEventAsync(httpContext.Response, ev, ct);
            }

            // 連続接続を維持するため keepalive comment を 30 秒ごとに送る
            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepaliveTask = Task.Run(async () =>
            {
                while (!keepaliveCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(30000, keepaliveCts.Token);
                        await httpContext.Response.WriteAsync(": keepalive\n\n", keepaliveCts.Token);
                        await httpContext.Response.Body.FlushAsync(keepaliveCts.Token);
                    }
                    catch { break; }
                }
            }, keepaliveCts.Token);

            try
            {
                await foreach (var ev in broker.SubscribeAsync(layerId, ct))
                {
                    await WriteEventAsync(httpContext.Response, ev, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            finally
            {
                keepaliveCts.Cancel();
                try { await keepaliveTask; } catch { }
            }
            return Results.Empty;
        }).RequireAuthorization();

        // F'101 (Phase F' WF'1): 複数 layer をまとめて購読する新 endpoint。
        //   GET /api/events/stream-all?layerIds=1,2,3
        //   layerIds の各 layer について can_view を検査、1 件でも不可なら 403。
        group.MapGet("/stream-all", async (
            string? layerIds,
            HttpContext httpContext,
            ICurrentUser user,
            ILayerPermissionService perm,
            ILayerInvalidationBroker broker,
            CancellationToken ct) =>
        {
            // ?layerIds=1,2,3 をパース
            if (string.IsNullOrWhiteSpace(layerIds))
            {
                return Results.BadRequest(new
                {
                    type = "https://docs.agri-gis/errors/missing-layer-ids",
                    title = "layerIds query parameter is required",
                    status = 400,
                    detail = "Specify ?layerIds=1,2,3"
                });
            }
            var ids = new List<int>();
            foreach (var s in layerIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(s, out var id))
                {
                    return Results.BadRequest(new
                    {
                        type = "https://docs.agri-gis/errors/invalid-layer-id",
                        title = "Invalid layerId",
                        status = 400,
                        detail = $"layerIds must be comma-separated integers, got '{s}'"
                    });
                }
                ids.Add(id);
            }
            if (ids.Count == 0)
            {
                return Results.BadRequest(new
                {
                    type = "https://docs.agri-gis/errors/empty-layer-ids",
                    title = "layerIds must be non-empty",
                    status = 400
                });
            }
            if (ids.Count > 100)
            {
                return Results.BadRequest(new
                {
                    type = "https://docs.agri-gis/errors/too-many-layer-ids",
                    title = "layerIds count limit 100",
                    status = 400,
                    detail = $"got {ids.Count}"
                });
            }

            // can_view 認可: 1 件でも view 不可なら 403
            foreach (var lid in ids)
            {
                if (!await perm.CanViewAsync(user.OrgId, lid, user.Roles, ct))
                {
                    return Results.Problem(
                        type: "https://docs.agri-gis/errors/layer-permission-denied",
                        title: "View denied for one or more layers",
                        detail: $"Your organization does not have can_view for layer {lid}.",
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }

            httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            httpContext.Response.Headers.Append("Cache-Control", "no-cache, no-store");
            httpContext.Response.Headers.Append("Connection", "keep-alive");
            httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

            // 直近 5 秒の event を replay (multi)
            foreach (var ev in broker.ReplayRecentMulti(ids, TimeSpan.FromSeconds(5)))
            {
                await WriteEventAsync(httpContext.Response, ev, ct);
            }

            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepaliveTask = Task.Run(async () =>
            {
                while (!keepaliveCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(30000, keepaliveCts.Token);
                        await httpContext.Response.WriteAsync(": keepalive\n\n", keepaliveCts.Token);
                        await httpContext.Response.Body.FlushAsync(keepaliveCts.Token);
                    }
                    catch { break; }
                }
            }, keepaliveCts.Token);

            try
            {
                await foreach (var ev in broker.SubscribeMultiAsync(ids, ct))
                {
                    await WriteEventAsync(httpContext.Response, ev, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            finally
            {
                keepaliveCts.Cancel();
                try { await keepaliveTask; } catch { }
            }
            return Results.Empty;
        }).RequireAuthorization();

        return group;
    }

    private static async Task WriteEventAsync(HttpResponse resp, LayerInvalidationEvent ev, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(ev);
        var sb = new StringBuilder();
        sb.Append("event: layer_invalidate\n");
        sb.Append("data: ").Append(json).Append("\n\n");
        await resp.WriteAsync(sb.ToString(), ct);
    }
}
