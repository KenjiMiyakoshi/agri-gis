using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A503: 旧 MissingActorTests のリネーム + assertion 更新。
// X-Actor 廃止 → JWT 必須。Bearer 無しなら 401 (旧 400)。
[Collection(PostgisCollection.Name)]
public sealed class AuthRequiredTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AuthRequiredTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_WithoutBearer_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).Build();

        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };

        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Patch_WithoutBearer_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).Build();

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { attributes = new Dictionary<string, object> { ["name"] = "Y" } })
        };
        req.Headers.TryAddWithoutValidation("If-Match", "1");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutBearer_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).Build();

        var res = await client.DeleteAsync($"/api/features/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PutSchema_WithoutBearer_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).Build();

        var body = new
        {
            schema = new
            {
                fields = new[]
                {
                    new { key = "name", type = "string", required = true, label = "観測点名" }
                }
            }
        };
        var res = await client.PutAsJsonAsync("/api/admin/layers/2/schema", body);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
