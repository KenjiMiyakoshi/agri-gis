using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerPermission;

// F205 (Phase F WF2): /tiles/{layerId}/... の can_view 検査 (深層防御)。
// 3 ケース:
//   1) general user で can_view=false → 403 (GeoServer 呼ばずに即時)
//   2) general user で can_view=true → 200/502/503 (GeoServer 呼ぶ経路)
//   3) admin role は権限関係なく GeoServer 経路 (bypass)
[Collection(PostgisCollection.Name)]
public sealed class TilesEndpointsCanViewTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public TilesEndpointsCanViewTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GeneralUser_CanViewFalse_Returns403()
    {
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 1,
            canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync("/tiles/1/default/15/29408/12051.png");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GeneralUser_CanViewTrue_ReachesGeoServerPath()
    {
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 1,
            canView: true, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync("/tiles/1/default/15/29408/12051.png");
        // GeoServer 経路に到達した = 403 ではない。Fake handler の結果は環境依存
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_BypassesPermission_ReachesGeoServerPath()
    {
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 1,
            canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync("/tiles/1/default/15/29408/12051.png");
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
