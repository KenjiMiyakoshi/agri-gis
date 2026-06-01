using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class LayerEndpoints
{
    public static RouteGroupBuilder MapLayerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at
                FROM layers
                ORDER BY layer_id";

            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();

            var rows = new List<object>();
            while (await r.ReadAsync())
            {
                rows.Add(new
                {
                    layerId = r.GetInt32(0),
                    layerName = r.GetString(1),
                    layerType = r.GetString(2),
                    ownerOrgId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    isShared = r.GetBoolean(4),
                    createdAt = r.GetDateTime(5)
                });
            }
            return Results.Ok(rows);
        });

        return group;
    }
}
