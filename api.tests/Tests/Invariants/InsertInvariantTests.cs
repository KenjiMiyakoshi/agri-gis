using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Invariants;

[Collection(PostgisCollection.Name)]
public sealed class InsertInvariantTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    private RowCounters _counters = null!;

    public InsertInvariantTests(PostgisContainerFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await DbReset.RunAsync(_pg.ConnectionString);
        _counters = new RowCounters(_pg.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_AddsCurrent_AndAuditOnly()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();

        var body = new
        {
            layerId = 2,                       // 観測点 (Point)
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };

        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // 不変条件: current=+1, history=0, audit=+1
        Assert.Equal(1, await _counters.CountCurrentAsync());
        Assert.Equal(0, await _counters.CountHistoryAsync());
        Assert.Equal(1, await _counters.CountAuditAsync());

        // audit の中身検査
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action, actor, before_doc, after_doc FROM audit_log ORDER BY audit_id DESC LIMIT 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal("feature_insert", r.GetString(0));
        Assert.Equal("alice", r.GetString(1));
        Assert.True(r.IsDBNull(2));            // before_doc は NULL
        Assert.False(r.IsDBNull(3));           // after_doc は NOT NULL
    }
}
