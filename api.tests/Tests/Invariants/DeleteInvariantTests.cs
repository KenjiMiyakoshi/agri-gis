using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Invariants;

[Collection(PostgisCollection.Name)]
public sealed class DeleteInvariantTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    private RowCounters _counters = null!;

    public DeleteInvariantTests(PostgisContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await DbReset.RunAsync(_pg.ConnectionString);
        _counters = new RowCounters(_pg.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Delete_RemovesCurrent_ArchivesToHistory_AddsAudit()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();

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

        // DELETE
        var delRes = await client.DeleteAsync($"/api/features/{entityId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // 不変条件: current=0, history=+1 (archived_reason='delete'), audit=+1
        Assert.Equal(0, await _counters.CountCurrentAsync());
        Assert.Equal(1, await _counters.CountHistoryAsync());
        Assert.Equal(2, await _counters.CountAuditAsync());

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using (var c = new NpgsqlCommand(
            "SELECT archived_reason FROM feature_history WHERE entity_id = @e", conn))
        {
            c.Parameters.AddWithValue("e", entityId);
            await using var r = await c.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("delete", r.GetString(0));
        }

        // 最新 audit: action=feature_delete, after_doc=NULL, before_doc NOT NULL
        await using (var c = new NpgsqlCommand(
            "SELECT action, before_doc, after_doc FROM audit_log ORDER BY audit_id DESC LIMIT 1", conn))
        {
            await using var r = await c.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("feature_delete", r.GetString(0));
            Assert.False(r.IsDBNull(1));
            Assert.True(r.IsDBNull(2));
        }
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);
}
