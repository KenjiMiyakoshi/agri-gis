using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A504: JWT 無効ケース (署名違い / 期限切れ / Bearer プレフィックス無し)
[Collection(PostgisCollection.Name)]
public sealed class JwtValidationTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public JwtValidationTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BadSignature_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var badToken = TokenForge.Issue(
            SeedUsers.AliceId, SeedUsers.AliceLogin, "Alice", SeedUsers.OrgId,
            new[] { "admin" },
            secret: "different-secret-32bytes-not-server-key!!");
        var client = new ApiClientFactory(api).WithBearer(badToken).Build();

        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var expired = TokenForge.Issue(
            SeedUsers.AliceId, SeedUsers.AliceLogin, "Alice", SeedUsers.OrgId,
            new[] { "admin" },
            ttl: TimeSpan.FromSeconds(-3600));
        var client = new ApiClientFactory(api).WithBearer(expired).Build();

        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task MalformedAuthorizationHeader_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "not-bearer-format");

        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
