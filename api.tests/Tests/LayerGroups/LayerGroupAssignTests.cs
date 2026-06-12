using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerGroups;

// LG106 (Phase LG WLG1): PUT /api/admin/layers/{layerId}/group (レイヤ配置) +
// GET /api/layers の groupId/sortOrder 拡張 + group 削除時のルート退避 (SET NULL)。
// seed layers: layer_id 1 = サンプル圃場, 2 = サンプル観測点 (DbReset)。
[Collection(PostgisCollection.Name)]
public sealed class LayerGroupAssignTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerGroupAssignTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AssignLayer_ThenDeleteGroup_MovesLayerToRoot()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var group = await CreateGroupAsync(admin, "賦課");

        // PUT: layer 1 をグループ配下へ
        var putRes = await admin.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = group.GroupId, sortOrder = 3 });
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var assigned = await putRes.Content.ReadFromJsonAsync<AssignRes>();
        Assert.Equal(1, assigned!.LayerId);
        Assert.Equal(group.GroupId, assigned.GroupId);
        Assert.Equal(3, assigned.SortOrder);

        // GET /api/layers に groupId / sortOrder が出る
        var layer = await GetLayerAsync(admin, 1);
        Assert.Equal(group.GroupId, layer.GroupId);
        Assert.Equal(3, layer.SortOrder);

        // group 削除 → 所属 layer は ON DELETE SET NULL でルート退避
        var delRes = await admin.DeleteAsync($"/api/admin/layer-groups/{group.GroupId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var afterDelete = await GetLayerAsync(admin, 1);
        Assert.Null(afterDelete.GroupId);
        Assert.Equal(3, afterDelete.SortOrder); // sort_order は退避後も保持
    }

    [Fact]
    public async Task Assign_NullGroupId_MovesLayerToRoot()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var group = await CreateGroupAsync(admin, "G1");
        await admin.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = group.GroupId, sortOrder = 1 });

        // groupId = null でルート直下へ戻す
        var rootRes = await admin.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = (int?)null, sortOrder = 7 });
        Assert.Equal(HttpStatusCode.OK, rootRes.StatusCode);

        var layer = await GetLayerAsync(admin, 1);
        Assert.Null(layer.GroupId);
        Assert.Equal(7, layer.SortOrder);
    }

    [Fact]
    public async Task Assign_NotFound_Cases_Return404()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // 不存在 layer
        var layerRes = await admin.PutAsJsonAsync("/api/admin/layers/9999/group",
            new { groupId = (int?)null, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.NotFound, layerRes.StatusCode);

        // 不存在 group
        var groupRes = await admin.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = 9999, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.NotFound, groupRes.StatusCode);
    }

    [Fact]
    public async Task GetLayers_AsGeneralUser_IncludesGroupId()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var bob = new ApiClientFactory(api).WithActorAs("bob", "general").Build();

        var group = await CreateGroupAsync(admin, "共有グループ");
        await admin.PutAsJsonAsync("/api/admin/layers/2/group",
            new { groupId = group.GroupId, sortOrder = 4 });

        // 一般 user の GET /api/layers (org_layer_permission JOIN 経路) でも groupId が出る
        var layer = await GetLayerAsync(bob, 2);
        Assert.Equal(group.GroupId, layer.GroupId);
        Assert.Equal(4, layer.SortOrder);

        // 未配置 layer は groupId = null / sortOrder = 0
        var unassigned = await GetLayerAsync(bob, 1);
        Assert.Null(unassigned.GroupId);
        Assert.Equal(0, unassigned.SortOrder);
    }

    private static async Task<GroupRes> CreateGroupAsync(HttpClient admin, string name)
    {
        var res = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GroupRes>())!;
    }

    private static async Task<LayerRes> GetLayerAsync(HttpClient client, int layerId)
    {
        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var layers = await res.Content.ReadFromJsonAsync<List<LayerRes>>();
        return layers!.Single(l => l.LayerId == layerId);
    }

    private sealed record GroupRes(int GroupId, int? ParentGroupId, string GroupName, int SortOrder);
    private sealed record LayerRes(int LayerId, string LayerName, int? GroupId, int SortOrder);
    private sealed record AssignRes(int LayerId, int? GroupId, int SortOrder);
}
