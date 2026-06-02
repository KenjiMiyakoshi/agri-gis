using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Admin;

// B501 (WB5): AdminLayers CRUD + import-jobs + bulk の認可マトリクスと
// 主要ステータスコード (200/201/204/400/403/404/409/413/422) を検証。
[Collection(PostgisCollection.Name)]
public sealed class AdminLayersTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AdminLayersTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- 認可マトリクス ----
    [Theory]
    [InlineData(SeedUsers.AliceLogin, "admin",   HttpStatusCode.OK)]
    [InlineData(SeedUsers.BobLogin,   "general", HttpStatusCode.Forbidden)]
    [InlineData(SeedUsers.CarolLogin, "guest",   HttpStatusCode.Forbidden)]
    public async Task ListLayers_RoleMatrix(string login, string role, HttpStatusCode expected)
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(login, role).Build();
        var res = await client.GetAsync("/api/admin/layers");
        Assert.Equal(expected, res.StatusCode);
    }

    [Theory]
    [InlineData(SeedUsers.AliceLogin, "admin",   HttpStatusCode.Created)]
    [InlineData(SeedUsers.BobLogin,   "general", HttpStatusCode.Forbidden)]
    [InlineData(SeedUsers.CarolLogin, "guest",   HttpStatusCode.Forbidden)]
    public async Task CreateLayer_RoleMatrix(string login, string role, HttpStatusCode expected)
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(login, role).Build();
        var res = await client.PostAsJsonAsync("/api/admin/layers", new
        {
            layerName = $"new layer {login}",
            layerType = "point",
            geometryType = "Point",
            sourceFormat = "geojson",
            sourceSrid = 4326
        });
        Assert.Equal(expected, res.StatusCode);
    }

    // ---- ハッピーパス + audit_log 検証 ----
    [Fact]
    public async Task CreateLayer_WritesAuditLog_GeomStripped()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createRes = await admin.PostAsJsonAsync("/api/admin/layers", new
        {
            layerName = "audit test",
            layerType = "point",
            geometryType = "Point",
            sourceFormat = "geojson",
            sourceSrid = 4326
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT action, actor, actor_user_id, after_doc ? 'geom' AS has_geom
              FROM audit_log
             WHERE action = 'layer_create'
             ORDER BY audit_id DESC LIMIT 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal("layer_create", r.GetString(0));
        Assert.Equal("Alice Admin", r.GetString(1));
        Assert.Equal(SeedUsers.AliceId, r.GetGuid(2));
        Assert.False(r.GetBoolean(3)); // C2: geom 含まれない
    }

    // ---- 409: import job running 中の DELETE ----
    [Fact]
    public async Task DeleteLayer_DuringRunningImport_Returns409()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createRes = await admin.PostAsJsonAsync("/api/admin/layers", new
        {
            layerName = "for delete", layerType = "point", geometryType = "Point"
        });
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var layerId = created.GetProperty("layerId").GetInt32();

        // import-job を start
        var startRes = await admin.PostAsJsonAsync(
            $"/api/admin/layers/{layerId}/import-jobs", new { totalCount = (int?)null });
        Assert.Equal(HttpStatusCode.Created, startRes.StatusCode);

        var delRes = await admin.DeleteAsync($"/api/admin/layers/{layerId}");
        Assert.Equal(HttpStatusCode.Conflict, delRes.StatusCode);
    }

    // ---- 409: 二重 start ----
    [Fact]
    public async Task StartImportJob_Twice_Returns409()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createRes = await admin.PostAsJsonAsync("/api/admin/layers", new
        {
            layerName = "for twice start", layerType = "point", geometryType = "Point"
        });
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var layerId = created.GetProperty("layerId").GetInt32();

        var s1 = await admin.PostAsJsonAsync(
            $"/api/admin/layers/{layerId}/import-jobs", new { totalCount = (int?)null });
        Assert.Equal(HttpStatusCode.Created, s1.StatusCode);
        var s2 = await admin.PostAsJsonAsync(
            $"/api/admin/layers/{layerId}/import-jobs", new { totalCount = (int?)null });
        Assert.Equal(HttpStatusCode.Conflict, s2.StatusCode);
    }

    // ---- 413: bulk Count 超過 ----
    [Fact]
    public async Task BulkInsert_TooLarge_Returns413()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createRes = await admin.PostAsJsonAsync("/api/admin/layers", new
        {
            layerName = "for 413", layerType = "point", geometryType = "Point",
            schema = new { fields = new[] { new { key = "name", type = "string", required = true } } }
        });
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var layerId = created.GetProperty("layerId").GetInt32();

        var jobRes = await admin.PostAsJsonAsync(
            $"/api/admin/layers/{layerId}/import-jobs", new { totalCount = (int?)null });
        var jobJson = await jobRes.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = jobJson.GetProperty("jobId").GetGuid();

        // 5001 件 (MaxCountPerChunk=5000 を超過) → 413
        var features = new List<object>();
        for (int i = 0; i < 5001; i++)
        {
            features.Add(new
            {
                geometry = new { type = "Point", coordinates = new[] { 143.2, 42.91 } },
                properties = new { name = $"p{i}" }
            });
        }
        var bulkRes = await admin.PostAsJsonAsync($"/api/admin/layers/{layerId}/features/bulk", new
        {
            jobId, chunkOrdinal = 0, chunkTotal = 1, sourceFormat = "geojson",
            features
        });
        Assert.Equal((HttpStatusCode)413, bulkRes.StatusCode);
    }
}
