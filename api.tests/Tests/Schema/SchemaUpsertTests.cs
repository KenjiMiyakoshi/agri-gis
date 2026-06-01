using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Schema;

[Collection(PostgisCollection.Name)]
public sealed class SchemaUpsertTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public SchemaUpsertTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SchemaUpsert_TwoTimes_BumpsVersionTo3_AndClosesOldRows()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();

        var schemaV2 = new
        {
            schema = new
            {
                fields = new[]
                {
                    new { key = "name", type = "string", required = true, label = "圃場名" },
                    new { key = "area", type = "number", required = false, label = "面積" }
                }
            }
        };
        var schemaV3 = new
        {
            schema = new
            {
                fields = new[]
                {
                    new { key = "name", type = "string", required = true, label = "圃場名" },
                    new { key = "area", type = "number", required = false, label = "面積" },
                    new { key = "crop", type = "string", required = false, label = "作物" }
                }
            }
        };

        var put1 = await client.PutAsJsonAsync("/api/admin/layers/1/schema", schemaV2);
        Assert.Equal(HttpStatusCode.OK, put1.StatusCode);
        var put1Body = await put1.Content.ReadFromJsonAsync<UpsertRes>();
        Assert.Equal(2, put1Body!.SchemaVersion);

        var put2 = await client.PutAsJsonAsync("/api/admin/layers/1/schema", schemaV3);
        Assert.Equal(HttpStatusCode.OK, put2.StatusCode);
        var put2Body = await put2.Content.ReadFromJsonAsync<UpsertRes>();
        Assert.Equal(3, put2Body!.SchemaVersion);

        // layers.schema_version が 3 になっている
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using (var c = new NpgsqlCommand("SELECT schema_version FROM layers WHERE layer_id = 1", conn))
        {
            var v = (int)(await c.ExecuteScalarAsync())!;
            Assert.Equal(3, v);
        }

        // layer_schema_version: 3 行 (v1/v2/v3)、最古 2 行に valid_to が入っている
        await using (var c = new NpgsqlCommand(
            "SELECT schema_version, valid_to FROM layer_schema_version WHERE layer_id = 1 ORDER BY schema_version", conn))
        {
            await using var r = await c.ExecuteReaderAsync();
            var rows = new List<(int Version, bool HasValidTo)>();
            while (await r.ReadAsync())
            {
                rows.Add((r.GetInt32(0), !r.IsDBNull(1)));
            }
            Assert.Equal(3, rows.Count);
            Assert.True(rows[0].HasValidTo);                  // v1 は閉じられた
            Assert.True(rows[1].HasValidTo);                  // v2 は閉じられた
            Assert.False(rows[2].HasValidTo);                 // v3 (現行) は valid_to=NULL
        }
    }

    private sealed record UpsertRes(int LayerId, int SchemaVersion);
}
