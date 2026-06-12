using AgriGis.Api.Services;
using AgriGis.Api.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Events;

// F'102/F'104 (Phase F' WF'1): PostgresLayerInvalidationBroker の Multi 系 + permission_invalidate を検証。
// NOTIFY 経路 (PostgreSQL LISTEN) はテストせず、broker の内部 publish 経路だけ単体で検証する。
[Collection(PostgisCollection.Name)]
public sealed class PermissionInvalidateBrokerTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public PermissionInvalidateBrokerTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private static PostgresLayerInvalidationBroker NewBroker(string cs)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AgriGis"] = cs
            })
            .Build();
        return new PostgresLayerInvalidationBroker(cfg);
    }

    [Fact]
    public async Task PublishPermissionInvalidate_FiresEventPerLayer()
    {
        await using var broker = NewBroker(_pg.ConnectionString);
        // StartAsync は呼ばない (LISTEN 不要、内部 publish のみ検証)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var received = new List<LayerInvalidationEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var ev in broker.SubscribeMultiAsync(new[] { 10, 20 }, cts.Token))
            {
                received.Add(ev);
                if (received.Count >= 2) break;
            }
        });

        // 購読開始を保証する遅延
        await Task.Delay(100);

        broker.PublishPermissionInvalidate(orgId: 5, affectedLayerIds: new[] { 10, 20 });

        await subscribeTask;

        Assert.Equal(2, received.Count);
        Assert.All(received, ev =>
        {
            Assert.Equal("permission", ev.Reason);
            Assert.Equal(5, ev.AffectedOrgId);
        });
        Assert.Contains(received, ev => ev.LayerId == 10);
        Assert.Contains(received, ev => ev.LayerId == 20);
    }

    [Fact]
    public async Task SubscribeMulti_FiltersToRequestedLayerIds()
    {
        await using var broker = NewBroker(_pg.ConnectionString);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var received = new List<LayerInvalidationEvent>();

        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var ev in broker.SubscribeMultiAsync(new[] { 10 }, cts.Token))
            {
                received.Add(ev);
                if (received.Count >= 1) break;
            }
        });

        await Task.Delay(100);

        // 10 と 20 の両方を fire するが、購読は 10 のみ → 1 件だけ受信される
        broker.PublishPermissionInvalidate(orgId: 5, affectedLayerIds: new[] { 10, 20 });

        await subscribeTask;

        Assert.Single(received);
        Assert.Equal(10, received[0].LayerId);
    }

    [Fact]
    public async Task ReplayRecentMulti_ReturnsRecentEventsForSubset()
    {
        await using var broker = NewBroker(_pg.ConnectionString);

        broker.PublishPermissionInvalidate(orgId: 5, affectedLayerIds: new[] { 10, 20, 30 });

        var subset = broker.ReplayRecentMulti(new[] { 10, 30 }, TimeSpan.FromSeconds(5)).ToList();
        Assert.Equal(2, subset.Count);
        Assert.Contains(subset, ev => ev.LayerId == 10);
        Assert.Contains(subset, ev => ev.LayerId == 30);
        Assert.DoesNotContain(subset, ev => ev.LayerId == 20);
        await Task.CompletedTask;
    }
}
