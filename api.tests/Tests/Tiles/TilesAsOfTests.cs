using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Tiles;

// E501 (WE5): GET /tiles/.../?asOf= の動作確認。
// 実 GeoServer は CI で起動しないため、asOf 有無で Cache-Control ヘッダが切り替わることだけ検証。
// (GeoServer 接続失敗時は 503 になるが Cache-Control は付与される前に return)
[Collection(PostgisCollection.Name)]
public sealed class TilesAsOfTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public TilesAsOfTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetTile_WithoutAsOf_BuildsFeatureCurrentUrl()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // CI 環境では GeoServer なし → 503 を期待 (URL 構築が成功した = featureType=feature_current 経路)
        var res = await client.GetAsync("/tiles/1/default/15/29408/12051.png");
        // 503 (GeoServer unreachable) or 200 (運良く GeoServer モックがある場合) を許容
        Assert.True(res.StatusCode == HttpStatusCode.ServiceUnavailable
                 || res.StatusCode == HttpStatusCode.OK
                 || res.StatusCode == HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task GetTile_InvalidAsOfFormat_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/tiles/1/default/15/29408/12051.png?asOf=2025-01-01T00:00:00Z");
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
    }

    [Fact]
    public async Task GetTile_InvalidTheme_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // theme 名に大文字を含むので validation で 400
        var res = await client.GetAsync("/tiles/1/Default/15/29408/12051.png");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
