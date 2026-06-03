using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Bitemporal;

// E501 (WE5): PATCH /api/admin/layers/{id} の fn_layer_update 経由化検証。
// PATCH 後に layer_history に旧版が退避され、version が +1 されることを確認。
[Collection(PostgisCollection.Name)]
public sealed class LayerUpdateBitemporalTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public LayerUpdateBitemporalTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PatchLayer_VersionIncrementsAndHistoryAppends()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // 初期 version 確認
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT version FROM layers WHERE layer_id = 1", conn);
            var v = (int)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(1, v);
        }

        // PATCH (If-Match なし → 内部 SELECT で expected_version=1)
        var patchBody = new { layerName = "サンプル圃場 v2" };
        var patchRes = await client.PatchAsJsonAsync("/api/admin/layers/1", patchBody);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // version=2 + layer_history に v1 退避を確認
        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var verCmd = new NpgsqlCommand(
                "SELECT version, layer_name FROM layers WHERE layer_id = 1", conn);
            await using var r = await verCmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(2, r.GetInt32(0));
            Assert.Equal("サンプル圃場 v2", r.GetString(1));
            await r.CloseAsync();

            await using var hisCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM layer_history WHERE layer_id = 1 AND archived_reason = 'update'", conn);
            var n = (long)(await hisCmd.ExecuteScalarAsync())!;
            Assert.Equal(1L, n);
        }
    }

    [Fact]
    public async Task PatchLayer_IfMatchWrongVersion_Returns409Conflict()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var req = new HttpRequestMessage(HttpMethod.Patch, "/api/admin/layers/1")
        {
            Content = JsonContent.Create(new { layerName = "wrong version" })
        };
        req.Headers.TryAddWithoutValidation("If-Match", "999");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}
