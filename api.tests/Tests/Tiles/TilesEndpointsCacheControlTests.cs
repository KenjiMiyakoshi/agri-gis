using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Tiles;

// E'302 (WE'3): Cache-Control header の検証。
// Phase D' D'102: asOf 無し時は max-age=86400, immutable
//                 asOf あり時は no-store (履歴 cache 肥大化防止)
[Collection(PostgisCollection.Name)]
public sealed class TilesEndpointsCacheControlTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public TilesEndpointsCacheControlTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TileWithoutAsOf_HasMaxAge86400_Immutable()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/tiles/1/default/12/3645/1612.png?sv=1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var cc = res.Headers.CacheControl;
        Assert.NotNull(cc);
        Assert.Equal(TimeSpan.FromDays(1), cc!.MaxAge);
        Assert.True(cc.Public);
        // immutable directive (cc.Extensions に no-name extension で含まれる)
        Assert.Contains(cc.Extensions, e => e.Name.Equals("immutable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TileWithAsOf_HasNoStore()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/tiles/1/default/12/3645/1612.png?asOf=2025-01-01");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var cc = res.Headers.CacheControl;
        Assert.NotNull(cc);
        Assert.True(cc!.NoStore);
        Assert.True(cc.NoCache);
        Assert.True(cc.MustRevalidate);
    }

    [Fact]
    public async Task TileWithBothSvAndAsOf_AsOfWins_NoStore()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/tiles/1/default/12/3645/1612.png?sv=3&asOf=2025-01-01");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var cc = res.Headers.CacheControl;
        Assert.NotNull(cc);
        Assert.True(cc!.NoStore);  // asOf 優先
    }
}
