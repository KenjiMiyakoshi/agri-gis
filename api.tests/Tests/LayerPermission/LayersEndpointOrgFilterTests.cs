using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerPermission;

// F201 (Phase F WF2): GET /api/layers の org_layer_permission フィルタ。
// 3 ケース:
//   1) admin は全件返却 (filter bypass) + canEdit=true
//   2) general user は can_view=true の layer のみ返却、canEdit は権限テーブル準拠
//   3) general user の can_view=false の layer は完全に消える
[Collection(PostgisCollection.Name)]
public sealed class LayersEndpointOrgFilterTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public LayersEndpointOrgFilterTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record LayerResponse(int LayerId, string LayerName, bool CanEdit);

    [Fact]
    public async Task Admin_ReturnsAllLayers_WithCanEditTrue()
    {
        // DbReset.SeedAsync は 2 layer を seed、Admin は permission 行が無くても全件返却
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 1, canView: false, canEdit: false);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2, canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var layers = await res.Content.ReadFromJsonAsync<List<LayerResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(layers);
        Assert.Equal(2, layers!.Count);
        Assert.All(layers, l => Assert.True(l.CanEdit));
    }

    [Fact]
    public async Task GeneralUser_OnlySeesCanViewLayers_WithCanEditReflected()
    {
        // layer 1: view+edit、layer 2: view のみ
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, 1, canView: true, canEdit: true);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, 2, canView: true, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var layers = await res.Content.ReadFromJsonAsync<List<LayerResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(layers);
        Assert.Equal(2, layers!.Count);
        Assert.True(layers.Single(l => l.LayerId == 1).CanEdit);
        Assert.False(layers.Single(l => l.LayerId == 2).CanEdit);
    }

    [Fact]
    public async Task GeneralUser_CanViewFalse_LayerInvisible()
    {
        // layer 1: hidden、layer 2: view のみ
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, 1, canView: false, canEdit: false);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, 2, canView: true, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var layers = await res.Content.ReadFromJsonAsync<List<LayerResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(layers);
        Assert.Single(layers!);
        Assert.Equal(2, layers![0].LayerId);
        Assert.False(layers[0].CanEdit);
    }
}
