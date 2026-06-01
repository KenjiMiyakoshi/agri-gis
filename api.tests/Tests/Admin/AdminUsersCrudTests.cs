using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Admin;

// A506: /api/admin/users CRUD ハッピーパス + 論理削除後の login_id 再利用 + password reset
[Collection(PostgisCollection.Name)]
public sealed class AdminUsersCrudTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AdminUsersCrudTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Crud_HappyPath()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var created = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            loginId = "dave",
            displayName = "Dave User",
            orgId = SeedUsers.OrgId,
            roles = new[] { "general" },
            initialPassword = "InitialPw123!"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<UserRes>();
        Assert.NotNull(dto);
        Assert.Equal("dave", dto!.LoginId);
        Assert.Contains("general", dto.Roles);

        var listRes = await admin.GetAsync("/api/admin/users");
        var list = await listRes.Content.ReadFromJsonAsync<List<UserRes>>();
        Assert.Contains(list!, u => u.LoginId == "dave");

        // ロール変更
        var patchRes = await admin.PatchAsJsonAsync($"/api/admin/users/{dto.UserId}",
            new { roles = new[] { "admin" } });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var updated = await patchRes.Content.ReadFromJsonAsync<UserRes>();
        Assert.Contains("admin", updated!.Roles);
        Assert.DoesNotContain("general", updated.Roles);

        // パスワードリセット → 新パスワードでログイン成功
        var resetRes = await admin.PutAsJsonAsync($"/api/admin/users/{dto.UserId}/password",
            new { newPassword = "NewPassword456!" });
        Assert.Equal(HttpStatusCode.NoContent, resetRes.StatusCode);

        var anon = api.CreateClient();
        var loginRes = await anon.PostAsJsonAsync("/api/auth/login",
            new { loginId = "dave", password = "NewPassword456!" });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // DELETE
        var delRes = await admin.DeleteAsync($"/api/admin/users/{dto.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }

    [Fact]
    public async Task LogicalDelete_AllowsLoginIdReuse()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var c1 = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            loginId = "ephemeral",
            displayName = "First",
            orgId = SeedUsers.OrgId,
            roles = new[] { "general" },
            initialPassword = "Password123!"
        });
        Assert.Equal(HttpStatusCode.Created, c1.StatusCode);
        var dto1 = await c1.Content.ReadFromJsonAsync<UserRes>();

        await admin.DeleteAsync($"/api/admin/users/{dto1!.UserId}");

        var c2 = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            loginId = "ephemeral",
            displayName = "Second",
            orgId = SeedUsers.OrgId,
            roles = new[] { "guest" },
            initialPassword = "Password123!"
        });
        Assert.Equal(HttpStatusCode.Created, c2.StatusCode);
    }

    private sealed record UserRes(
        Guid UserId, string LoginId, string DisplayName, int OrgId,
        List<string> Roles, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
