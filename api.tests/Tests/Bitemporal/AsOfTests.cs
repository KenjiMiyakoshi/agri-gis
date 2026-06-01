using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Bitemporal;

[Collection(PostgisCollection.Name)]
public sealed class AsOfTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AsOfTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    // WA5/A503: 手動 UPDATE 削除版。C1 修復で同日 Insert+Patch は history 区間 [今日, 今日) のゼロ幅となる。
    // 過去日付 asOf は history も current もヒットしない（仕様）ことを確認する。
    [Fact]
    public async Task AsOf_SameDayInsertAndPatch_PastDateReturnsEmpty()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createBody = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var createRes = await client.PostAsJsonAsync("/api/features", createBody);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreatedRes>();
        var entityId = Guid.Parse(created!.EntityId);

        var patchBody = new
        {
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.21,42.91]}").RootElement
        };
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(patchBody)
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", "1");
        var patchRes = await client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // 現在 (asOf 無し) → 新図形 v=2 のみ
        var currentRes = await client.GetAsync("/api/features?layerId=2");
        var currentFc = await currentRes.Content.ReadFromJsonAsync<FeatureCollection>();
        Assert.NotNull(currentFc);
        var currentFeat = Assert.Single(currentFc!.Features);
        Assert.Equal(2, currentFeat.Properties.Version);

        // 過去 (asOf=3 日前) → 同日 Insert+Patch は history 区間 [今日, 今日) なので 0 件
        var pastDate = DateTime.UtcNow.Date.AddDays(-3).ToString("yyyy-MM-dd");
        var pastRes = await client.GetAsync($"/api/features?layerId=2&asOf={pastDate}");
        Assert.Equal(HttpStatusCode.OK, pastRes.StatusCode);
        var pastFc = await pastRes.Content.ReadFromJsonAsync<FeatureCollection>();
        Assert.NotNull(pastFc);
        Assert.Empty(pastFc!.Features);
    }

    [Fact]
    public async Task AsOf_WithIsoDatetime_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/api/features?layerId=1&asOf=2026-01-01T00:00:00Z");
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);

    private sealed record FeatureCollection(string Type, object? Crs, List<Feature> Features);
    private sealed record Feature(string Type, JsonElement Geometry, FeatureProps Properties);
    private sealed record FeatureProps(long FeatureId, int LayerId, string EntityId, int Version);
}
