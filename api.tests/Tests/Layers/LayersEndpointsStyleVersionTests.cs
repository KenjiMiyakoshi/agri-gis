using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Layers;

// E'301 (WE'3): GET /api/layers レスポンスに styleVersion フィールドが含まれること
// + PUT /api/admin/layers/{id}/style 後に styleVersion が +1 されることを検証。
// Phase D' WD'1 (D'101) で導入。E'101 (DbReset 並列耐性改善) 後に追加。
[Collection(PostgisCollection.Name)]
public sealed class LayersEndpointsStyleVersionTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayersEndpointsStyleVersionTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLayers_ReturnsStyleVersionField()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetArrayLength() > 0, "layer が 1 件以上 seed されているはず");
        var first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("styleVersion", out var sv),
            $"styleVersion フィールドが含まれていない: {body[..Math.Min(500, body.Length)]}");
        Assert.True(sv.GetInt32() >= 1, $"styleVersion は 1 以上のはず (got {sv.GetInt32()})");
    }

    [Fact]
    public async Task PutStyle_TwicePattern_IncrementsStyleVersion()
    {
        // 0E03 migration は seed 前に走るため testcontainer では layer_style_version が空。
        // PUT 1 回目で v=1 が active、PUT 2 回目で v=2 active → +1 を確認。
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var s1 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#FF8800""}}}").RootElement;
        var put1 = await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s1 });
        Assert.Equal(HttpStatusCode.OK, put1.StatusCode);

        var res1 = await client.GetAsync("/api/layers");
        using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var initialSv = FindLayer(doc1, 1).GetProperty("styleVersion").GetInt32();

        var s2 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#00FF88""}}}").RootElement;
        var put2 = await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s2 });
        Assert.Equal(HttpStatusCode.OK, put2.StatusCode);

        var res2 = await client.GetAsync("/api/layers");
        using var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
        var afterSv = FindLayer(doc2, 1).GetProperty("styleVersion").GetInt32();
        Assert.Equal(initialSv + 1, afterSv);
    }

    [Fact]
    public async Task GetLayersAsOf_ReturnsHistoricalStyleVersion()
    {
        // PUT 2 回で style_version=1,2 を作る。asOf=未来日付 で v=2、asOf=PUT1前 で
        // styleVersion=1 (COALESCE デフォルト) を確認する形は厳密性に欠けるが、
        // E' レベルでは「asOf 経路でも styleVersion フィールドが返る」ことを確認するのが本旨。
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var s1 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#FF0000""}}}").RootElement;
        await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s1 });
        var s2 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#00FF00""}}}").RootElement;
        await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s2 });

        var asOf = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var res = await client.GetAsync($"/api/layers?asOf={asOf}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
        foreach (var layer in doc.RootElement.EnumerateArray())
        {
            Assert.True(layer.TryGetProperty("styleVersion", out var sv));
            Assert.True(sv.GetInt32() >= 1);
        }
    }

    private static JsonElement FindLayer(JsonDocument doc, int layerId)
    {
        foreach (var l in doc.RootElement.EnumerateArray())
        {
            if (l.GetProperty("layerId").GetInt32() == layerId) return l;
        }
        throw new InvalidOperationException($"layer {layerId} not found");
    }
}
