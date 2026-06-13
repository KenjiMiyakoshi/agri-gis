using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerGroups;

// LGP106 (Phase LG' WLGP1): layer_group_member による組織別レイヤ配置 +
// GET /api/layers の groupId/sortOrder が member 経由になったこと + backfill 検証。
// seed layers: layer_id 1 = サンプル圃場, 2 = サンプル観測点 (DbReset)。
[Collection(PostgisCollection.Name)]
public sealed class LayerGroupMemberTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerGroupMemberTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record GroupRes(int GroupId, int? ParentGroupId, string GroupName, int SortOrder);
    private sealed record LayerRes(int LayerId, string LayerName, int? GroupId, int SortOrder);

    [Fact]
    public async Task SameLayer_PlacedDifferently_PerOrg()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);
        // org B も layer 1/2 を閲覧可に (共有レイヤ)
        await DbReset.SetPermissionAsync(_pg.ConnectionString, orgB.OrgId, 1, canView: true, canEdit: true);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, orgB.OrgId, 2, canView: true, canEdit: true);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var adminB = BuildOrgBAdmin(api, orgB);

        // 各 org が独立した group を作る
        var groupA = await CreateGroupAsync(adminA, "群1A");
        var groupB = await CreateGroupAsync(adminB, "群2B");

        // 同一 layer 1 を org A は groupA に、org B は groupB に配置
        await AssignAsync(adminA, 1, groupA.GroupId, 5);
        await AssignAsync(adminB, 1, groupB.GroupId, 9);

        // org A の GET /api/layers では layer 1 が groupA
        var layerForA = await GetLayerAsync(adminA, 1);
        Assert.Equal(groupA.GroupId, layerForA.GroupId);
        Assert.Equal(5, layerForA.SortOrder);

        // org B の GET /api/layers では同じ layer 1 が groupB
        var layerForB = await GetLayerAsync(adminB, 1);
        Assert.Equal(groupB.GroupId, layerForB.GroupId);
        Assert.Equal(9, layerForB.SortOrder);

        // 未配置の layer 2 は両 org で groupId null / sortOrder 0
        var l2A = await GetLayerAsync(adminA, 2);
        Assert.Null(l2A.GroupId);
        Assert.Equal(0, l2A.SortOrder);
        var l2B = await GetLayerAsync(adminB, 2);
        Assert.Null(l2B.GroupId);
        Assert.Equal(0, l2B.SortOrder);
    }

    [Fact]
    public async Task Assign_OtherOrgGroup_Returns404()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);
        await DbReset.SetPermissionAsync(_pg.ConnectionString, orgB.OrgId, 1, canView: true, canEdit: true);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var adminB = BuildOrgBAdmin(api, orgB);

        var groupA = await CreateGroupAsync(adminA, "群A");

        // org B admin が org A の group に layer を配置しようとする → 404
        var res = await adminB.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = groupA.GroupId, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Assign_LayerNotViewableByOrg_Returns404()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);
        // org B には layer の閲覧権限を与えない

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminB = BuildOrgBAdmin(api, orgB);

        var groupB = await CreateGroupAsync(adminB, "群B");

        // org B は layer 1 を閲覧不可 → 配置しようとしても 404
        var res = await adminB.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = groupB.GroupId, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // backfill 検証: テスト fixture は空 DB から migration を流すため、
    // ここでは「新規作成した group が既定組織 (alice の org) に紐づくこと」と
    // 「PUT で配置した layer が既定組織 member に入ること」を直接 SQL で確認する。
    [Fact]
    public async Task Group_And_Member_BelongToDefaultOrg()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var groupA = await CreateGroupAsync(adminA, "賦課");
        await AssignAsync(adminA, 1, groupA.GroupId, 2);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        // group の org_id = 既定組織
        await using (var c = new NpgsqlCommand(
            "SELECT org_id FROM layer_group WHERE group_id = @id", conn))
        {
            c.Parameters.AddWithValue("id", groupA.GroupId);
            var orgId = Convert.ToInt32(await c.ExecuteScalarAsync());
            Assert.Equal(SeedUsers.OrgId, orgId);
        }

        // member 行の org_id = 既定組織
        await using (var c = new NpgsqlCommand(
            "SELECT org_id, group_id, sort_order FROM layer_group_member WHERE layer_id = 1", conn))
        {
            await using var r = await c.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(SeedUsers.OrgId, r.GetInt32(0));
            Assert.Equal(groupA.GroupId, r.GetInt32(1));
            Assert.Equal(2, r.GetInt32(2));
        }
    }

    private HttpClient BuildOrgBAdmin(ApiFactory api, DbReset.SecondOrg orgB)
    {
        var token = TokenForge.Issue(
            userId: orgB.UserId,
            loginId: orgB.LoginId,
            displayName: orgB.DisplayName,
            orgId: orgB.OrgId,
            roles: new[] { "admin" },
            connectionString: _pg.ConnectionString);
        return new ApiClientFactory(api).WithBearer(token).Build();
    }

    private static async Task<GroupRes> CreateGroupAsync(HttpClient admin, string name)
    {
        var res = await admin.PostAsJsonAsync("/api/admin/layer-groups", new { groupName = name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GroupRes>())!;
    }

    private static async Task AssignAsync(HttpClient admin, int layerId, int? groupId, int sortOrder)
    {
        var res = await admin.PutAsJsonAsync($"/api/admin/layers/{layerId}/group",
            new { groupId, sortOrder });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private static async Task<LayerRes> GetLayerAsync(HttpClient client, int layerId)
    {
        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var layers = await res.Content.ReadFromJsonAsync<List<LayerRes>>();
        return layers!.Single(l => l.LayerId == layerId);
    }
}
