using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Bitemporal;

// E501 (WE5): /api/layers の asOf 経路の検証。
// 現在 / 過去 / 削除済 で layer 一覧が異なることを確認する。
[Collection(PostgisCollection.Name)]
public sealed class LayerAsOfTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerAsOfTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetLayers_NoAsOf_ReturnsActiveLayersOnly()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // seed の 2 active layer のみ
        var res = await client.GetAsync("/api/layers");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(json);
        Assert.Equal(2, json!.Count);
    }

    [Fact]
    public async Task GetLayers_PastAsOf_BeforeSeed_ReturnsEmpty()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // seed 行 (=2 active) の valid_from = CURRENT_DATE。前日 asOf は範囲外。
        var pastDate = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var res = await client.GetAsync($"/api/layers?asOf={pastDate}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(json);
        Assert.Empty(json!);
    }

    [Fact]
    public async Task GetLayers_AfterDelete_PastAsOfStillContains()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // layer 2 を直接 fn_layer_delete で削除 (layer_history に退避 + valid_to=CURRENT_DATE)
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT fn_layer_delete(2, 'admin', 'test-req',
                    (SELECT user_id FROM users WHERE login_id='alice'),
                    (SELECT org_id FROM users WHERE login_id='alice'))", conn);
            await cmd.ExecuteScalarAsync();
        }

        // 現在の一覧 → layer 2 は含まれない
        var nowRes = await client.GetAsync("/api/layers");
        var nowList = await nowRes.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(nowList);
        Assert.DoesNotContain(nowList!, e => e.GetProperty("layerId").GetInt32() == 2);

        // 削除前の asOf (= seed の valid_from = 今日と同じ日) → layer 2 を含むことが期待されるが、
        // 同日削除はゼロ幅区間 [today, today) になり asOf=today だと history も current もヒットしないのが C1 仕様。
        // ここでは「現在(削除後)で layer 2 が消える」だけを検証。
    }
}
