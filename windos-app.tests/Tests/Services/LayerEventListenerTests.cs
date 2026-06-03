using System.Net;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Services;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services;

// E'404 (WE'4): LayerEventListener の最小動作検証。
// 自動 reconnect の指数バックオフテストは flaky になりやすいので、本テストでは
// 「SSE 形式のストリームを正しくパースして Received イベントを発火する」ことのみ確認。
public sealed class LayerEventListenerTests
{
    [Fact]
    public async Task Subscribe_ParsesSse_AndFiresReceivedEvent()
    {
        // ストリーム body: 2 イベント (1 件目 = layer_invalidate, 2 件目 = keepalive コメント + style)
        var sseBody = "event: layer_invalidate\n" +
                      "data: {\"layerId\":1,\"reason\":\"feature\",\"action\":\"update\",\"occurredAt\":\"2026-06-03T13:00:00Z\"}\n\n" +
                      ": keepalive\n\n" +
                      "event: layer_invalidate\n" +
                      "data: {\"layerId\":1,\"reason\":\"style\",\"styleVersion\":5,\"occurredAt\":\"2026-06-03T13:01:00Z\"}\n\n";

        var handler = new ScriptedHandler(sseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var session = new InMemorySessionStore();
        session.Set(new Session(
            AccessToken: "test-token",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            UserId: Guid.NewGuid(),
            LoginId: "alice",
            DisplayName: "Alice",
            OrgId: 1,
            Roles: new[] { "admin" }));

        using var listener = new LayerEventListener(http, session);
        var received = new List<LayerInvalidationEvent>();
        listener.Received += (_, ev) => received.Add(ev);
        listener.Subscribe(1);

        // ストリームが流れ終わるまで wait (最大 1s)
        for (int i = 0; i < 20 && received.Count < 2; i++)
            await Task.Delay(50);

        listener.Unsubscribe();

        Assert.True(received.Count >= 2, $"expected 2 events, got {received.Count}");
        Assert.Equal("feature", received[0].Reason);
        Assert.Equal("update", received[0].Action);
        Assert.Equal("style", received[1].Reason);
        Assert.Equal(5, received[1].StyleVersion);
    }

    [Fact]
    public void Unsubscribe_BeforeStart_DoesNotThrow()
    {
        var http = new HttpClient(new ScriptedHandler("")) { BaseAddress = new Uri("http://localhost") };
        var session = new InMemorySessionStore();
        using var listener = new LayerEventListener(http, session);
        // 例外が出ないこと
        listener.Unsubscribe();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly string _body;
        public ScriptedHandler(string body) { _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(resp);
        }
    }
}
