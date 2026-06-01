using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Concurrency;

[Collection(PostgisCollection.Name)]
public sealed class OptimisticLockTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public OptimisticLockTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Patch_WithoutIfMatch_Returns428()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();
        var entityId = await CreateAsync(client);

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(new { attributes = new Dictionary<string, object> { ["name"] = "Y" } })
        };
        var res = await client.SendAsync(req);
        Assert.Equal(428, (int)res.StatusCode);
    }

    [Fact]
    public async Task Patch_WithWrongIfMatch_Returns409_WithRequestId()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").WithRequestId("rid-test-1").Build();
        var entityId = await CreateAsync(client);

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(new { attributes = new Dictionary<string, object> { ["name"] = "Y" } })
        };
        req.Headers.TryAddWithoutValidation("If-Match", "99");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // ProblemDetails の status と requestId
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());

        // requestId は top-level または extensions の両方をチェック (実装依存)
        var rid = root.TryGetProperty("requestId", out var topRid)
            ? topRid.GetString()
            : (root.TryGetProperty("extensions", out var ext) &&
               ext.TryGetProperty("requestId", out var extRid)
                ? extRid.GetString()
                : null);
        Assert.Equal("rid-test-1", rid);
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
