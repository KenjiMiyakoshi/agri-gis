using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Validation;

[Collection(PostgisCollection.Name)]
public sealed class MissingActorTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public MissingActorTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_WithoutXActor_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).Build();  // X-Actor 無し

        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };

        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Patch_WithoutXActor_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        // 事前 POST は actor 付きで
        var withActor = new ApiClientFactory(api).WithActor("alice").Build();
        var entityId = await CreateAsync(withActor);

        // 本テスト: actor 無しで PATCH
        var noActor = new ApiClientFactory(api).Build();
        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(new { attributes = new Dictionary<string, object> { ["name"] = "Y" } })
        };
        req.Headers.TryAddWithoutValidation("If-Match", "1");
        var res = await noActor.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutXActor_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var withActor = new ApiClientFactory(api).WithActor("alice").Build();
        var entityId = await CreateAsync(withActor);

        var noActor = new ApiClientFactory(api).Build();
        var res = await noActor.DeleteAsync($"/api/features/{entityId}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PutSchema_WithoutXActor_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var noActor = new ApiClientFactory(api).Build();

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
        var res = await noActor.PutAsJsonAsync("/api/admin/layers/2/schema", body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<CreatedRes>();
        return Guid.Parse(created!.EntityId);
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);
}
