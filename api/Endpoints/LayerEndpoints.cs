using AgriGis.Api.Dto;
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

            // 注: schema_version / schema_json の SELECT は #16 (0205) で本格対応する。
            // この時点ではプレースホルダ値を返す（DTO 型を満たすため）。
            var emptySchema = new LayerSchemaDto(Array.Empty<SchemaFieldDto>());

            var rows = new List<LayerDto>();
            while (await r.ReadAsync())
            {
                var createdAt = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
                rows.Add(new LayerDto(
                    LayerId: r.GetInt32(0),
                    LayerName: r.GetString(1),
                    LayerType: r.GetString(2),
                    OwnerOrgId: r.IsDBNull(3) ? null : r.GetInt32(3),
                    IsShared: r.GetBoolean(4),
                    CreatedAt: new DateTimeOffset(createdAt, TimeSpan.Zero),
                    SchemaVersion: 1,
                    Schema: emptySchema
                ));
            }
            return Results.Ok(rows);
        });

        return group;
    }
}
