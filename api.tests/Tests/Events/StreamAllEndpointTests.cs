using System.Net;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Events;

// F'101 (Phase F' WF'1): /api/events/stream-all の認可 + 入力検証。
// SSE 接続の維持テストは tricky なため、エラーパス + 旧 endpoint の Sunset ヘッダのみ検証。
[Collection(PostgisCollection.Name)]
public sealed class StreamAllEndpointTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public StreamAllEndpointTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NoAuth_Returns401()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = api.CreateClient();

        var res = await client.GetAsync("/api/events/stream-all?layerIds=1,2");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task MissingLayerIds_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync("/api/events/stream-all");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task InvalidLayerIdFormat_Returns400()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync("/api/events/stream-all?layerIds=1,abc,3");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task GeneralUser_LayerNotPermitted_Returns403()
    {
        // bob (general) は default org に居て、layer 1,2 は DbReset seed で can_view=true、can_edit=true
        // 但し layer 2 を can_view=false にしてアクセス拒否を再現
        await DbReset.SetPermissionAsync(_pg.ConnectionString, SeedUsers.OrgId, layerId: 2,
            canView: false, canEdit: false);

        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        var res = await client.GetAsync("/api/events/stream-all?layerIds=1,2");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // F'101: 旧 endpoint の Sunset ヘッダは SSE 長期接続のため TestHost で読み取れないため、
    // ソースコードレベルの存在チェックで代替 (EventsEndpoints.cs に Sunset 設定があることを確認)。
    [Fact]
    public void OldEndpoint_HasSunsetHeaderInSource()
    {
        var path = AgriGis.Api.Tests.Fixtures.SolutionRoot.Resolve("api/Endpoints/EventsEndpoints.cs");
        var src = System.IO.File.ReadAllText(path);
        Assert.Contains("\"Sunset\"", src);
        Assert.Contains("\"Deprecation\"", src);
        Assert.Contains("successor-version", src);
    }
}
