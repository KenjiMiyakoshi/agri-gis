using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Bitemporal;

// A508/C1 修復検証: PATCH/DELETE 後の feature_history.valid_to = CURRENT_DATE、
// feature_current.valid_from = CURRENT_DATE で半開区間が接合されていること（手動 UPDATE なし）
[Collection(PostgisCollection.Name)]
public sealed class C1RegressionTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public C1RegressionTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Patch_HistoryValidTo_EqualsToday_CurrentValidFrom_EqualsToday()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createBody = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var createRes = await client.PostAsJsonAsync("/api/features", createBody);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreatedRes>();
        var entityId = Guid.Parse(created!.EntityId);

        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}")
        {
            Content = JsonContent.Create(new
            {
                geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.3,42.91]}").RootElement
            })
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", "1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(patchReq)).StatusCode);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        // history: valid_to = today
        await using (var c = new NpgsqlCommand(
            "SELECT valid_to FROM feature_history WHERE entity_id = @e", conn))
        {
            c.Parameters.AddWithValue("e", entityId);
            var validTo = (DateTime)(await c.ExecuteScalarAsync())!;
            Assert.Equal(DateTime.UtcNow.Date, validTo.Date);
        }

        // current: valid_from = today
        await using (var c = new NpgsqlCommand(
            "SELECT valid_from FROM feature_current WHERE entity_id = @e", conn))
        {
            c.Parameters.AddWithValue("e", entityId);
            var validFrom = (DateTime)(await c.ExecuteScalarAsync())!;
            Assert.Equal(DateTime.UtcNow.Date, validFrom.Date);
        }
    }

    [Fact]
    public async Task Delete_HistoryValidTo_EqualsToday()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var createBody = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var createRes = await client.PostAsJsonAsync("/api/features", createBody);
        var created = await createRes.Content.ReadFromJsonAsync<CreatedRes>();
        var entityId = Guid.Parse(created!.EntityId);

        var delRes = await client.DeleteAsync($"/api/features/{entityId}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var c = new NpgsqlCommand(
            "SELECT valid_to FROM feature_history WHERE entity_id = @e", conn);
        c.Parameters.AddWithValue("e", entityId);
        var validTo = (DateTime)(await c.ExecuteScalarAsync())!;
        Assert.Equal(DateTime.UtcNow.Date, validTo.Date);
    }

    private sealed record CreatedRes(long FeatureId, string EntityId, int Version, int AttributesSchemaVersion);
}
