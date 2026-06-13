using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerGroups;

// LGP106 (Phase LG' WLGP1): layer_group の組織スコープ。
// 組織ごとに完全独立したツリーになり、admin は自 org のツリーのみ管理する。
//   - org A の group は org B の GET /api/layer-groups に出ない
//   - org B admin が org A の group を PATCH / DELETE → 404 (越権遮断)
//   - org B admin が作った group は org B のみに出る
[Collection(PostgisCollection.Name)]
public sealed class LayerGroupOrgScopeTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerGroupOrgScopeTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record GroupRes(int GroupId, int? ParentGroupId, string GroupName, int SortOrder);

    [Fact]
    public async Task Groups_AreIsolatedPerOrg()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var adminB = BuildOrgBAdmin(api, orgB);

        // org A admin が group を作成
        var aGroup = await CreateGroupAsync(adminA, "賦課A");

        // org B admin が group を作成
        var bGroup = await CreateGroupAsync(adminB, "測量B");

        // org A の GET は自 org の group のみ
        var listA = await GetGroupsAsync(adminA);
        Assert.Single(listA);
        Assert.Equal(aGroup.GroupId, listA[0].GroupId);
        Assert.Equal("賦課A", listA[0].GroupName);

        // org B の GET は自 org の group のみ (org A の group は見えない)
        var listB = await GetGroupsAsync(adminB);
        Assert.Single(listB);
        Assert.Equal(bGroup.GroupId, listB[0].GroupId);
        Assert.Equal("測量B", listB[0].GroupName);
    }

    [Fact]
    public async Task OrgB_CannotPatchOrDeleteOrgAGroup_Returns404()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var adminB = BuildOrgBAdmin(api, orgB);

        var aGroup = await CreateGroupAsync(adminA, "賦課A");

        // org B admin が org A の group を PATCH → 404
        var patchRes = await adminB.PatchAsJsonAsync($"/api/admin/layer-groups/{aGroup.GroupId}",
            new { groupName = "改ざん" });
        Assert.Equal(HttpStatusCode.NotFound, patchRes.StatusCode);

        // org B admin が org A の group を DELETE → 404
        var delRes = await adminB.DeleteAsync($"/api/admin/layer-groups/{aGroup.GroupId}");
        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);

        // org A の group は無傷
        var listA = await GetGroupsAsync(adminA);
        Assert.Single(listA);
        Assert.Equal("賦課A", listA[0].GroupName);
    }

    [Fact]
    public async Task OrgB_CannotParentUnderOrgAGroup_Returns404()
    {
        var orgB = await DbReset.SeedSecondOrgAsync(_pg.ConnectionString);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var adminA = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var adminB = BuildOrgBAdmin(api, orgB);

        var aGroup = await CreateGroupAsync(adminA, "賦課A");

        // org B admin が org A の group を親に指定して POST → 404 (他 org の parent 不可)
        var postRes = await adminB.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "子", parentGroupId = aGroup.GroupId });
        Assert.Equal(HttpStatusCode.NotFound, postRes.StatusCode);
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

    private static async Task<List<GroupRes>> GetGroupsAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/layer-groups");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<List<GroupRes>>())!;
    }
}
