using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerPermission;

// F204 (Phase F WF2): /api/features POST に can_edit 検査。
// 3 ケース:
//   1) can_edit=false の layer に対し general user → 403
//   2) can_edit=true の layer に対し general user → 201 Created
//   3) admin role は権限関係なく 201 (bypass)
[Collection(PostgisCollection.Name)]
public sealed class FeatureEndpointsCanEditTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public FeatureEndpointsCanEditTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private static object BuildFeatureBody(int layerId, string name) => new
    {
        layerId,
        geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
        attributes = new Dictionary<string, object> { ["name"] = name }
    };

    [Fact]
    public async Task GeneralUser_CanEditFalse_Returns403()
    {
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2,
            canView: true, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.PostAsJsonAsync("/api/features", BuildFeatureBody(layerId: 2, "foo"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GeneralUser_CanEditTrue_Returns201()
    {
        // 既定 seed が can_edit=true、明示再設定不要だが念のため
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2,
            canView: true, canEdit: true);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.PostAsJsonAsync("/api/features", BuildFeatureBody(layerId: 2, "bar"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task Admin_BypassesPermission_Returns201()
    {
        // admin org の権限を全 false に設定しても、admin role なら通る
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2,
            canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.PostAsJsonAsync("/api/features", BuildFeatureBody(layerId: 2, "baz"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}
