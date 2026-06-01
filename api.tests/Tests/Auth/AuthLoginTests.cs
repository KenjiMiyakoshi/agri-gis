using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A504: POST /api/auth/login の成功/失敗
[Collection(PostgisCollection.Name)]
public sealed class AuthLoginTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AuthLoginTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { loginId = SeedUsers.AliceLogin, password = SeedUsers.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<LoginRes>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.Equal(SeedUsers.AliceLogin, body.User.LoginId);
        Assert.Contains("admin", body.User.Roles);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { loginId = SeedUsers.AliceLogin, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { loginId = "no-such-user", password = SeedUsers.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private sealed record LoginRes(string AccessToken, DateTime ExpiresAt, UserRes User);
    private sealed record UserRes(Guid UserId, string LoginId, string DisplayName, int OrgId, List<string> Roles);
}
