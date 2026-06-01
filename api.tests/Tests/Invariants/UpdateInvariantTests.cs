using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Invariants;

[Collection(PostgisCollection.Name)]
public sealed class UpdateInvariantTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    private RowCounters _counters = null!;

    public UpdateInvariantTests(PostgisContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await DbReset.RunAsync(_pg.ConnectionString);
        _counters = new RowCounters(_pg.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Update_MovesOldToHistory_BumpsVersion_AddsAudit()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        // 事前 POST
        var createBody = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var createRes = await client.PostAsJsonAsync("/api/features", createBody);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreatedRes>();
        Assert.NotNull(created);
        var entityId = Guid.Parse(created!.EntityId);

        // PATCH (属性のみ更新、If-Match=1)
        var patchBody = new
        {
            attributes = new Dictionary<string, object> { ["name"] = "Y" }
        };
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(patchBody)
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", "1");
        var patchRes = await client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // 不変条件: current=1 (但し version=2), history=+1 (version=1, archived_reason='update'), audit=+1
        Assert.Equal(1, await _counters.CountCurrentAsync());
        Assert.Equal(1, await _counters.CountHistoryAsync());
        Assert.Equal(2, await _counters.CountAuditAsync());
        Assert.Equal(2, await _counters.MaxVersionAsync(entityId));

        // history の中身
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using (var c = new NpgsqlCommand(
            "SELECT version, archived_reason FROM feature_history WHERE entity_id = @e", conn))
        {
            c.Parameters.AddWithValue("e", entityId);
            await using var r = await c.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(1, r.GetInt32(0));
            Assert.Equal("update", r.GetString(1));
        }

        // 最新の audit が feature_update で before/after 両方 NOT NULL
        await using (var c = new NpgsqlCommand(
            "SELECT action, before_doc, after_doc FROM audit_log ORDER BY audit_id DESC LIMIT 1", conn))
        {
            await using var r = await c.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("feature_update", r.GetString(0));
            Assert.False(r.IsDBNull(1));
            Assert.False(r.IsDBNull(2));
        }
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);
}
