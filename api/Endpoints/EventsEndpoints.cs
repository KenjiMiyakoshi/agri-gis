using System.Text;
using System.Text.Json;
using AgriGis.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace AgriGis.Api.Endpoints;

// D'301 (WD'3): Server-Sent Events で layer 無効化イベントを配信。
// GET /api/events/layers/{layerId}/stream
// - text/event-stream で永続接続
// - 直近 5 秒の event を replay buffer から先に送る
// - 以降は ILayerInvalidationBroker から push される event をフィルタ (layerId 一致) で送信
//
// 認証: EventSource は Authorization ヘッダを送れないため、Program.cs の
// JwtBearer.OnMessageReceived で ?access_token= からも JWT を受領する分岐を追加する。
public static class EventsEndpoints
{
    public static RouteGroupBuilder MapEventsEndpoints(this RouteGroupBuilder group)
    {
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
