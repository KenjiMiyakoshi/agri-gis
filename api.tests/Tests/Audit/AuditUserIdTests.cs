using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Audit;

// A507: feature_insert 後の audit_log.actor_user_id が seed ユーザの user_id と一致する
[Collection(PostgisCollection.Name)]
public sealed class AuditUserIdTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AuditUserIdTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FeatureInsert_PopulatesActorUserId()
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
            @"SELECT actor_user_id, actor_org_id, actor
                FROM audit_log
               WHERE action = 'feature_insert'
               ORDER BY audit_id DESC
               LIMIT 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());

        var actorUserId = r.GetGuid(0);
        var actorOrgId = r.GetInt32(1);
        var actor = r.GetString(2);

        Assert.Equal(SeedUsers.AliceId, actorUserId);
        Assert.Equal(SeedUsers.OrgId, actorOrgId);
        Assert.Equal("Alice Admin", actor);
    }
}
