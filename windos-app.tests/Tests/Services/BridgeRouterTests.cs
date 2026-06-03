using System.Text.Json;
using AgriGis.Desktop.Services;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services;

// H5-204 (WH5-2): BridgeRouter のテスト。
public sealed class BridgeRouterTests
{
    [Fact]
    public async Task DispatchAsync_RegisteredType_CallsHandler()
    {
        var router = new BridgeRouter();
        var called = false;
        router.Register("features_selected", (payload, ct) =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var env = MakeEnvelope("features_selected", "{\"x\":1}");
        await router.DispatchAsync(env, CancellationToken.None);
        Assert.True(called);
    }

    [Fact]
    public async Task DispatchAsync_UnknownType_DoesNothing()
    {
        var router = new BridgeRouter();
        var env = MakeEnvelope("never_registered", "{}");
        // 例外も出ず、OnError も呼ばれないこと
        var errorCalled = false;
        router.OnError = (_, _) => errorCalled = true;
        await router.DispatchAsync(env, CancellationToken.None);
        Assert.False(errorCalled);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_CallsOnError()
    {
        var router = new BridgeRouter();
        var caughtType = "";
        Exception? caughtEx = null;
        router.OnError = (type, ex) => { caughtType = type; caughtEx = ex; };
        router.Register("explode", (_, _) => throw new InvalidOperationException("boom"));

        var env = MakeEnvelope("explode", "{}");
        await router.DispatchAsync(env, CancellationToken.None);

        Assert.Equal("explode", caughtType);
        Assert.IsType<InvalidOperationException>(caughtEx);
    }

    [Fact]
    public void Register_DuplicateType_LastWins()
    {
        var router = new BridgeRouter();
        router.Register("x", (_, _) => Task.CompletedTask);
        router.Register("x", (_, _) => Task.CompletedTask);
        Assert.Single(router.RegisteredTypes);
        Assert.Contains("x", router.RegisteredTypes);
    }

    private static Envelope MakeEnvelope(string type, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new Envelope(type, doc.RootElement.Clone(), null);
    }
}
