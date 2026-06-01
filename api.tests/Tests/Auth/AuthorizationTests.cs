using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A505: 3 ロール × エンドポイント matrix
[Collection(PostgisCollection.Name)]
public sealed class AuthorizationTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AuthorizationTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    // GET /api/layers は全ロール OK (認証必須)
    [Theory]
    [InlineData(SeedUsers.AliceLogin, "admin")]
    [InlineData(SeedUsers.BobLogin,   "general")]
    [InlineData(SeedUsers.CarolLogin, "guest")]
    public async Task LayersGet_AllRoles_OK(string login, string role)
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(login, role).Build();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // POST /api/features は admin/general のみ、guest は 403
    [Theory]
    [InlineData(SeedUsers.AliceLogin, "admin",   HttpStatusCode.Created)]
    [InlineData(SeedUsers.BobLogin,   "general", HttpStatusCode.Created)]
    [InlineData(SeedUsers.CarolLogin, "guest",   HttpStatusCode.Forbidden)]
    public async Task FeaturesPost_RoleMatrix(string login, string role, HttpStatusCode expected)
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(login, role).Build();

        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(expected, res.StatusCode);
    }

    // /api/admin/users は admin のみ、general/guest は 403
    [Theory]
    [InlineData(SeedUsers.AliceLogin, "admin",   HttpStatusCode.OK)]
    [InlineData(SeedUsers.BobLogin,   "general", HttpStatusCode.Forbidden)]
    [InlineData(SeedUsers.CarolLogin, "guest",   HttpStatusCode.Forbidden)]
    public async Task AdminUsersGet_RoleMatrix(string login, string role, HttpStatusCode expected)
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(login, role).Build();

        var res = await client.GetAsync("/api/admin/users");
        Assert.Equal(expected, res.StatusCode);
    }
}
