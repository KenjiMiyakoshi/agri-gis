using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Admin;

// E501 (WE5): PUT /api/admin/layers/{id}/style が fn_layer_style_upsert 経由になり
// layer_style_version に履歴を append することを確認。
// 注: 0E03 migration は seed 前に走るため、CI 環境では layer_style_version は初期 0 件。
//     PUT 1 回目で style_version=1、2 回目で v=1 closed + v=2 active となる。
[Collection(PostgisCollection.Name)]
public sealed class StyleHistoryTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public StyleHistoryTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PutStyle_Twice_AppendsTwoVersions()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // PUT 1 回目 → style_version=1 が active
        var s1 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#FF0000""}}}").RootElement;
        var put1 = await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s1 });
        Assert.Equal(HttpStatusCode.OK, put1.StatusCode);

        // PUT 2 回目 → style_version=2 が active、v=1 が closed
        var s2 = JsonDocument.Parse(@"{""themes"":{""default"":{""fillColor"":""#00FF00""}}}").RootElement;
        var put2 = await client.PutAsJsonAsync("/api/admin/layers/1/style", new { styleJson = s2 });
        Assert.Equal(HttpStatusCode.OK, put2.StatusCode);

        // 2 行 (v1=closed, v2=active) を確認
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT style_version, valid_to FROM layer_style_version
             WHERE layer_id = 1
             ORDER BY style_version", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(1, r.GetInt32(0));
        var v1ValidTo = r.GetDateTime(1);
        Assert.True(v1ValidTo < DateTime.MaxValue.AddDays(-1));  // closed (valid_to != '9999-12-31')
        Assert.True(await r.ReadAsync());
        Assert.Equal(2, r.GetInt32(0));
        var v2ValidTo = r.GetDateTime(1);
        Assert.Equal(new DateTime(9999, 12, 31), v2ValidTo);  // active
    }
}
