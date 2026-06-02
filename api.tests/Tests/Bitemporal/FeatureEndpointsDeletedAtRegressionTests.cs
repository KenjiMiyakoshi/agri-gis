using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Bitemporal;

// B502 (WB3): 論理削除レイヤの feature が /api/features* で見えないこと (案 C 致命 3 回帰)
[Collection(PostgisCollection.Name)]
public sealed class FeatureEndpointsDeletedAtRegressionTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public FeatureEndpointsDeletedAtRegressionTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeletedLayer_FeaturesGet_ReturnsEmpty()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // 3 件 feature を layer 2 (サンプル観測点) に作成
        var entityIds = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var body = new
            {
                layerId = 2,
                geometry = JsonDocument.Parse($"{{\"type\":\"Point\",\"coordinates\":[143.{20+i},42.91]}}").RootElement,
                attributes = new Dictionary<string, object> { ["name"] = $"X{i}" }
            };
            var res = await admin.PostAsJsonAsync("/api/features", body);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var created = await res.Content.ReadFromJsonAsync<CreatedRes>();
            entityIds.Add(Guid.Parse(created!.EntityId));
        }

        // 削除前は GET で 3 件返る
        var beforeRes = await admin.GetAsync("/api/features?layerId=2");
        var beforeFc = await beforeRes.Content.ReadFromJsonAsync<FeatureCollection>();
        Assert.Equal(3, beforeFc!.Features.Count);

        // layer 2 を論理削除 (DB 直接 UPDATE で代用、AdminLayers DELETE は別途テスト)
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE layers SET deleted_at = now() WHERE layer_id = 2", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 削除後の GET 一覧 → 0 件
        var afterRes = await admin.GetAsync("/api/features?layerId=2");
        var afterFc = await afterRes.Content.ReadFromJsonAsync<FeatureCollection>();
        Assert.Empty(afterFc!.Features);

        // 単体 GET → 404
        var singleRes = await admin.GetAsync($"/api/features/{entityIds[0]}");
        Assert.Equal(HttpStatusCode.NotFound, singleRes.StatusCode);

        // history GET → 0 件
        var historyRes = await admin.GetAsync($"/api/features/{entityIds[0]}/history");
        Assert.Equal(HttpStatusCode.OK, historyRes.StatusCode);
        var historyList = await historyRes.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Empty(historyList!);

        // DELETE → 404
        var delRes = await admin.DeleteAsync($"/api/features/{entityIds[1]}");
        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);

        // POST → 404 (layers WHERE id AND deleted_at IS NULL でヒットしない)
        var newPostBody = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse("{\"type\":\"Point\",\"coordinates\":[143.30,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "Y" }
        };
        var postRes = await admin.PostAsJsonAsync("/api/features", newPostBody);
        Assert.Equal(HttpStatusCode.NotFound, postRes.StatusCode);
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);
    private sealed record FeatureCollection(string Type, object? Crs, List<JsonElement> Features);
}
