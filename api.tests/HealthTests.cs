using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests;

[Collection(PostgisCollection.Name)]
public sealed class HealthTests
{
    private readonly PostgisContainerFixture _pg;

    public HealthTests(PostgisContainerFixture pg)
    {
        _pg = pg;
    }

    [Fact]
    public async Task GetHealth_returns_200_and_status_ok()
    {
        // 共通フィクスチャから接続文字列を取り、API を立ち上げる
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.GetAsync("/api/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }
}
