using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Audit;

// A507/C2 修復検証: audit_log.before_doc/after_doc に geom (hex EWKB 文字列) が
// 焼き込まれず、geom_geojson キーに GeoJSON オブジェクトが入ること
[Collection(PostgisCollection.Name)]
public sealed class AuditLogGeomStripTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AuditLogGeomStripTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FeatureInsert_AfterDoc_HasGeomGeojsonNotRawGeom()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT after_doc::text
                FROM audit_log
               WHERE action = 'feature_insert'
               ORDER BY audit_id DESC LIMIT 1", conn);
        var afterDocStr = (string)(await cmd.ExecuteScalarAsync())!;

        using var doc = JsonDocument.Parse(afterDocStr);
        var root = doc.RootElement;

        // geom は除外されている
        Assert.False(root.TryGetProperty("geom", out _),
            "after_doc should not contain raw 'geom' (PostGIS EWKB)");

        // geom_geojson に GeoJSON オブジェクトが入っている
        Assert.True(root.TryGetProperty("geom_geojson", out var gj));
        Assert.Equal(JsonValueKind.Object, gj.ValueKind);
        Assert.Equal("Point", gj.GetProperty("type").GetString());
    }
}
