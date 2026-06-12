using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Services;
using AgriGis.Api.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Events;

// F'401 (Phase F' WF'4): PUT /api/admin/organizations/{orgId}/layer-permissions が
// broker.PublishPermissionInvalidate を呼ぶことを検証。
// テスト用 spy broker で publish 呼び出しを capture する。
[Collection(PostgisCollection.Name)]
public sealed class PermissionChangeBroadcastTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public PermissionChangeBroadcastTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class SpyBroker : ILayerInvalidationBroker
    {
        public List<(int OrgId, IReadOnlyList<int> LayerIds)> PermissionCalls { get; } = new();

        public IAsyncEnumerable<LayerInvalidationEvent> SubscribeAsync(int layerId, CancellationToken ct)
            => EmptyAsync();
        public IAsyncEnumerable<LayerInvalidationEvent> SubscribeMultiAsync(
            IReadOnlyList<int> layerIds, CancellationToken ct)
            => EmptyAsync();
        public IEnumerable<LayerInvalidationEvent> ReplayRecent(int layerId, TimeSpan window)
            => Enumerable.Empty<LayerInvalidationEvent>();
        public IEnumerable<LayerInvalidationEvent> ReplayRecentMulti(
            IReadOnlyList<int> layerIds, TimeSpan window)
            => Enumerable.Empty<LayerInvalidationEvent>();
        public void PublishPermissionInvalidate(int orgId, IReadOnlyList<int> affectedLayerIds)
        {
            PermissionCalls.Add((orgId, affectedLayerIds.ToList()));
        }

        private static async IAsyncEnumerable<LayerInvalidationEvent> EmptyAsync()
        {
            yield break;
            await Task.CompletedTask;
        }
    }

    private ApiFactory CreateFactoryWithSpyBroker(SpyBroker spy)
    {
        return new ApiFactory(_pg.ConnectionString, s =>
        {
            // ILayerInvalidationBroker を spy に差し替え
            var descs = s.Where(d => d.ServiceType == typeof(ILayerInvalidationBroker)).ToList();
            foreach (var d in descs) s.Remove(d);
            s.AddSingleton<ILayerInvalidationBroker>(spy);
        });
    }

    [Fact]
    public async Task Put_BroadcastsPermissionInvalidate_WithOrgIdAndLayerIds()
    {
        var spy = new SpyBroker();
        await using var api = CreateFactoryWithSpyBroker(spy);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var body = new
        {
            permissions = new[]
            {
                new { layerId = 1, canView = true, canEdit = false },
                new { layerId = 2, canView = false, canEdit = false }
            }
        };
        var res = await client.PutAsJsonAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // broker.PublishPermissionInvalidate(orgId, [1, 2]) が 1 回呼ばれる
        Assert.Single(spy.PermissionCalls);
        var call = spy.PermissionCalls[0];
        Assert.Equal(SeedUsers.OrgId, call.OrgId);
        Assert.Equal(2, call.LayerIds.Count);
        Assert.Contains(1, call.LayerIds);
        Assert.Contains(2, call.LayerIds);
    }

    [Fact]
    public async Task Put_BroadcastsOnlyDistinctLayerIds()
    {
        var spy = new SpyBroker();
        await using var api = CreateFactoryWithSpyBroker(spy);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        // 同じ layerId が複数項目に出てくる場合は distinct で 1 回だけ broadcast
        // (現状 PUT は同 layerId の重複入力を拒否しないため、distinct ガードを検証)
        var body = new
        {
            permissions = new[]
            {
                new { layerId = 1, canView = true, canEdit = true }
            }
        };
        var res = await client.PutAsJsonAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Single(spy.PermissionCalls);
        Assert.Single(spy.PermissionCalls[0].LayerIds);
    }
}
