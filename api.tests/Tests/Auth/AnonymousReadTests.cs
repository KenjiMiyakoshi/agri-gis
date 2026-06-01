using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A504: /api/health は Bearer 無しでも 200、その他は Bearer 必須 (401)
[Collection(PostgisCollection.Name)]
public sealed class AnonymousReadTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AnonymousReadTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task LayersGet_Anonymous_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task FeaturesGet_Anonymous_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.GetAsync("/api/features?layerId=1");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
