using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.LayerGroups;

// LG106 (Phase LG WLG1): /api/admin/layer-groups CRUD + 循環 parent 422 + 認可。
// - admin CRUD ハッピーパス (201 + Location / 200 / 204、audit_log 記録)
// - PATCH parentGroupId の循環 (自分自身 / 自分の子孫) は 422
// - 非 admin (general) の書き込みは 403
// - GET /api/layer-groups は認証必須 (Bearer 無し 401、guest でも 200)
[Collection(PostgisCollection.Name)]
public sealed class LayerGroupsCrudTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerGroupsCrudTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Crud_HappyPath()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // POST: ルートグループ
        var rootRes = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "賦課", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, rootRes.StatusCode);
        var root = await rootRes.Content.ReadFromJsonAsync<GroupRes>();
        Assert.NotNull(root);
        Assert.True(root!.GroupId > 0);
        Assert.Null(root.ParentGroupId);
        Assert.Equal("賦課", root.GroupName);
        Assert.Equal(1, root.SortOrder);
        Assert.Equal($"/api/admin/layer-groups/{root.GroupId}",
            rootRes.Headers.Location?.ToString());

        // POST: 子グループ
        var childRes = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "賦課詳細", parentGroupId = root.GroupId });
        Assert.Equal(HttpStatusCode.Created, childRes.StatusCode);
        var child = await childRes.Content.ReadFromJsonAsync<GroupRes>();
        Assert.Equal(root.GroupId, child!.ParentGroupId);
        Assert.Equal(0, child.SortOrder); // sortOrder 省略時は 0

        // GET /api/layer-groups: authenticated (general でも見える)、sort_order, group_id 順
        var bob = new ApiClientFactory(api).WithActorAs("bob", "general").Build();
        var listRes = await bob.GetAsync("/api/layer-groups");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<List<GroupRes>>();
        Assert.Equal(2, list!.Count);
        Assert.Equal(child.GroupId, list[0].GroupId); // sort_order 0 が先
        Assert.Equal(root.GroupId, list[1].GroupId);  // sort_order 1

        // PATCH: rename + sortOrder
        var patchRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{child.GroupId}",
            new { groupName = "賦課地区", sortOrder = 5 });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var patched = await patchRes.Content.ReadFromJsonAsync<GroupRes>();
        Assert.Equal("賦課地区", patched!.GroupName);
        Assert.Equal(5, patched.SortOrder);
        Assert.Equal(root.GroupId, patched.ParentGroupId); // parentGroupId 未指定 = 変更なし

        // PATCH: parentGroupId = null でルート直下へ移動
        var toRootRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{child.GroupId}",
            new { parentGroupId = (int?)null });
        Assert.Equal(HttpStatusCode.OK, toRootRes.StatusCode);
        var movedToRoot = await toRootRes.Content.ReadFromJsonAsync<GroupRes>();
        Assert.Null(movedToRoot!.ParentGroupId);

        // PATCH: 別の親へ移動
        var backRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{child.GroupId}",
            new { parentGroupId = root.GroupId });
        Assert.Equal(HttpStatusCode.OK, backRes.StatusCode);

        // DELETE: 親を消すと子は CASCADE
        var delRes = await admin.DeleteAsync($"/api/admin/layer-groups/{root.GroupId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var afterRes = await admin.GetAsync("/api/layer-groups");
        var after = await afterRes.Content.ReadFromJsonAsync<List<GroupRes>>();
        Assert.Empty(after!);

        // audit_log: Tx 内 INSERT (action='layer_group_*') を確認
        Assert.True(await CountAuditAsync("layer_group_create") >= 2);
        Assert.True(await CountAuditAsync("layer_group_update") >= 3);
        Assert.Equal(1, await CountAuditAsync("layer_group_delete"));
    }

    [Fact]
    public async Task Patch_CircularParent_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var a = await CreateGroupAsync(admin, "A", null);
        var b = await CreateGroupAsync(admin, "B", a.GroupId);
        var c = await CreateGroupAsync(admin, "C", b.GroupId);

        // 自分の孫を parent にする → 422
        var grandRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{a.GroupId}",
            new { parentGroupId = c.GroupId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, grandRes.StatusCode);
        // ProblemDetails (extensions.errors に circular が載る)
        var problem = await grandRes.Content.ReadAsStringAsync();
        Assert.Contains("circular", problem);
        Assert.Contains("requestId", problem);

        // 自分自身を parent にする → 422
        var selfRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{a.GroupId}",
            new { parentGroupId = a.GroupId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfRes.StatusCode);

        // 422 で弾かれた変更は反映されていない
        var list = await (await admin.GetAsync("/api/layer-groups"))
            .Content.ReadFromJsonAsync<List<GroupRes>>();
        Assert.Null(list!.Single(g => g.GroupId == a.GroupId).ParentGroupId);
    }

    [Fact]
    public async Task NotFound_Cases_Return404()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // POST: 不存在 parent
        var postRes = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "孤児", parentGroupId = 9999 });
        Assert.Equal(HttpStatusCode.NotFound, postRes.StatusCode);

        // PATCH / DELETE: 不存在 group
        var patchRes = await admin.PatchAsJsonAsync("/api/admin/layer-groups/9999",
            new { groupName = "無" });
        Assert.Equal(HttpStatusCode.NotFound, patchRes.StatusCode);

        var delRes = await admin.DeleteAsync("/api/admin/layer-groups/9999");
        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);

        // PATCH: 不存在 parent への移動
        var a = await CreateGroupAsync(admin, "A", null);
        var moveRes = await admin.PatchAsJsonAsync($"/api/admin/layer-groups/{a.GroupId}",
            new { parentGroupId = 9999 });
        Assert.Equal(HttpStatusCode.NotFound, moveRes.StatusCode);
    }

    [Fact]
    public async Task Post_EmptyGroupName_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "  " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Write_Returns403()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();
        var bob = new ApiClientFactory(api).WithActorAs("bob", "general").Build();

        var a = await CreateGroupAsync(admin, "A", null);

        var postRes = await bob.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = "不正" });
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);

        var patchRes = await bob.PatchAsJsonAsync($"/api/admin/layer-groups/{a.GroupId}",
            new { groupName = "改ざん" });
        Assert.Equal(HttpStatusCode.Forbidden, patchRes.StatusCode);

        var delRes = await bob.DeleteAsync($"/api/admin/layer-groups/{a.GroupId}");
        Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);

        var putRes = await bob.PutAsJsonAsync("/api/admin/layers/1/group",
            new { groupId = a.GroupId, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, putRes.StatusCode);
    }

    [Fact]
    public async Task GetLayerGroups_RequiresAuthentication()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);

        // Bearer 無し → 401
        var anon = new ApiClientFactory(api).Build();
        var anonRes = await anon.GetAsync("/api/layer-groups");
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        // guest でも authenticated なら 200
        var carol = new ApiClientFactory(api).WithActorAs("carol", "guest").Build();
        var guestRes = await carol.GetAsync("/api/layer-groups");
        Assert.Equal(HttpStatusCode.OK, guestRes.StatusCode);
    }

    private static async Task<GroupRes> CreateGroupAsync(HttpClient admin, string name, int? parentGroupId)
    {
        var res = await admin.PostAsJsonAsync("/api/admin/layer-groups",
            new { groupName = name, parentGroupId });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GroupRes>())!;
    }

    private async Task<long> CountAuditAsync(string action)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM audit_log WHERE action = @a AND actor_user_id IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("a", action);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record GroupRes(int GroupId, int? ParentGroupId, string GroupName, int SortOrder);
}
