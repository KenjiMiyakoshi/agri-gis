using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGis.Desktop.Auth;

namespace AgriGis.Desktop.Services;

// E'401 (WE'4): Phase D' D'301 で実装した SSE エンドポイント
// (GET /api/events/layers/{layerId}/stream) を購読する WinForms 側 listener。
//
// 設計:
// - HttpClient + StreamReader で SSE を手書きパース (System.Net.ServerSentEvents は .NET 9+)
// - 認証は ?access_token= 経由 (EventSource は Authorization ヘッダ送れない問題と同じ理由を共有)
// - 自動 reconnect は指数バックオフ (1s → 2s → 4s → 8s → 16s → 30s 上限)
// - 受信イベントは Received イベントで配信、UI スレッドへの marshalling は呼び出し側責任
public sealed class LayerEventListener : IDisposable
{
    private readonly HttpClient _http;
    private readonly ISessionStore _session;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public LayerEventListener(HttpClient http, ISessionStore session)
    {
        _http = http;
        _session = session;
    }

    public event EventHandler<LayerInvalidationEvent>? Received;

    public void Subscribe(int layerId)
    {
        Unsubscribe();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(layerId, _cts.Token));
    }

    public void Unsubscribe()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task LoopAsync(int layerId, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeStreamAsync(layerId, ct);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LayerEventListener] {ex.Message}");
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return; }
                var nextSec = Math.Min(delay.TotalSeconds * 2, 30);
                delay = TimeSpan.FromSeconds(nextSec);
            }
        }
    }

    private async Task ConsumeStreamAsync(int layerId, CancellationToken ct)
    {
        var token = _session.Current?.AccessToken
            ?? throw new InvalidOperationException("no access token");
        var url = $"/api/events/layers/{layerId}/stream?access_token={Uri.EscapeDataString(token)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var dataSb = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.StartsWith(":"))
            {
                // SSE comment (keepalive) — 無視
            }
            else if (line.StartsWith("event:"))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                dataSb.Append(line["data:".Length..].Trim());
            }
            else if (string.IsNullOrEmpty(line))
            {
                // event 終端
                if (eventName == "layer_invalidate" && dataSb.Length > 0)
                {
                    DispatchEvent(dataSb.ToString());
                }
                eventName = null;
                dataSb.Clear();
            }
        }
    }

    private void DispatchEvent(string json)
    {
        try
        {
            var ev = JsonSerializer.Deserialize<LayerInvalidationEvent>(json);
            if (ev is not null) Received?.Invoke(this, ev);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LayerEventListener] parse: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Unsubscribe();
    }
}

public sealed record LayerInvalidationEvent(
    [property: JsonPropertyName("layerId")] int LayerId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("styleVersion")] int? StyleVersion,
    [property: JsonPropertyName("occurredAt")] DateTime OccurredAt);
