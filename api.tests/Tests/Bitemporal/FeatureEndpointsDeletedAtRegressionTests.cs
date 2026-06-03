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
    public async Task DeletedLayer_LayersGet_DoesNotInclude()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // 初期は 2 レイヤ
        var beforeRes = await client.GetAsync("/api/layers");
        var beforeList = await beforeRes.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Equal(2, beforeList!.Count);

        // layer 2 を論理削除
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE layers SET valid_to = CURRENT_DATE WHERE layer_id = 2", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 削除後の GET /api/layers → 1 件 (layer 1 のみ)
        var afterRes = await client.GetAsync("/api/layers");
        var afterList = await afterRes.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Single(afterList!);
        Assert.Equal(1, afterList![0].GetProperty("layerId").GetInt32());

        // GET /api/layers/2/schema → 404 (deleted_at IS NULL ヒットなし)
        var schemaRes = await client.GetAsync("/api/layers/2/schema");
        Assert.Equal(HttpStatusCode.NotFound, schemaRes.StatusCode);
    }

    // D504 (WD5): 削除前後の確認は ?layerId= 経路ではなく {entityId} 単発 GET で行う。
    // ?layerId= は Phase D で 410 Gone のため、bitemporal 検証は個別 entity でカバー。
    [Fact]
    public async Task DeletedLayer_FeaturesGet_ReturnsNotFound()
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

        // 削除前は単発 GET で 200
        foreach (var id in entityIds)
        {
            var res = await admin.GetAsync($"/api/features/{id}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // layer 2 を論理削除 (DB 直接 UPDATE で代用、AdminLayers DELETE は別途テスト)
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE layers SET valid_to = CURRENT_DATE WHERE layer_id = 2", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 削除後は全 entity の単発 GET が 404 (deleted_at IS NULL 条件で除外)
        foreach (var id in entityIds)
        {
            var res = await admin.GetAsync($"/api/features/{id}");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        }

        // 単体 GET → 404 (代表で 1 件再確認)
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
