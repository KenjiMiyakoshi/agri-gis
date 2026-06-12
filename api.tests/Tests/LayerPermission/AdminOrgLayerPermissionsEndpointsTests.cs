using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerPermission;

// F203 (Phase F WF2): /api/admin/organizations/{orgId}/layer-permissions の GET + PUT。
// 3 ケース:
//   1) GET admin → 全 active layer × 該当 org の (canView, canEdit) を返す
//   2) PUT admin → upsert + audit_log INSERT + 取得後値が反映されている
//   3) non-admin (general) → 403
[Collection(PostgisCollection.Name)]
public sealed class AdminOrgLayerPermissionsEndpointsTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public AdminOrgLayerPermissionsEndpointsTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PermResponse(int OrgId, int LayerId, string LayerName, string LayerType,
        bool CanView, bool CanEdit);

    [Fact]
    public async Task Admin_Get_ReturnsAllActiveLayersWithCurrentPermissions()
    {
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 1,
            canView: true, canEdit: false);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2,
            canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var perms = await res.Content.ReadFromJsonAsync<List<PermResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(perms);
        Assert.Equal(2, perms!.Count);
        Assert.True(perms.Single(p => p.LayerId == 1).CanView);
        Assert.False(perms.Single(p => p.LayerId == 2).CanView);
    }

    [Fact]
    public async Task Admin_Put_UpsertsAndReflectsValues()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var body = new
        {
            permissions = new[]
            {
                new { layerId = 1, canView = false, canEdit = false },
                new { layerId = 2, canView = true,  canEdit = true  }
            }
        };
        var res = await client.PutAsJsonAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var after = await res.Content.ReadFromJsonAsync<List<PermResponse>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(after);
        Assert.False(after!.Single(p => p.LayerId == 1).CanView);
        Assert.True(after.Single(p => p.LayerId == 2).CanEdit);
    }

    [Fact]
    public async Task NonAdmin_Get_Returns403()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Put_CanEditWithoutCanView_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var body = new
        {
            permissions = new[]
            {
                new { layerId = 1, canView = false, canEdit = true }
            }
        };
        var res = await client.PutAsJsonAsync(
            $"/api/admin/organizations/{SeedUsers.OrgId}/layer-permissions", body);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
    }
}
