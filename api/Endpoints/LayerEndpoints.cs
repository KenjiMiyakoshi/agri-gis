using System.Text.Json;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class LayerEndpoints
{
    public static RouteGroupBuilder MapLayerEndpoints(this RouteGroupBuilder group)
    {
        // 0205: GET /api/layers — schema_json と schema_version を含めて全レイヤを返す
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
                       schema_version, schema_json
                FROM layers
                ORDER BY layer_id";

            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();

            var rows = new List<LayerDto>();
            while (await r.ReadAsync())
            {
                var createdAt = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
                var schemaJson = r.GetString(7);
                var schema = JsonSerializer.Deserialize<LayerSchemaDto>(schemaJson, JsonOpts.Default)
                             ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());

                rows.Add(new LayerDto(
                    LayerId: r.GetInt32(0),
                    LayerName: r.GetString(1),
                    LayerType: r.GetString(2),
                    OwnerOrgId: r.IsDBNull(3) ? null : r.GetInt32(3),
                    IsShared: r.GetBoolean(4),
                    CreatedAt: new DateTimeOffset(createdAt, TimeSpan.Zero),
                    SchemaVersion: r.GetInt32(6),
                    Schema: schema
                ));
            }
            return Results.Ok(rows);
        });

        // 0206: GET /api/layers/{layerId}/schema — 個別レイヤの現行スキーマだけを返す
        group.MapGet("/{layerId:int}/schema", async (int layerId, NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT schema_version, schema_json
                FROM layers
                WHERE layer_id = @id";

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            await using var r = await cmd.ExecuteReaderAsync();

            if (!await r.ReadAsync())
            {
                throw new NotFoundException($"layer not found: {layerId}");
            }

            var schemaJson = r.GetString(1);
            var schema = JsonSerializer.Deserialize<LayerSchemaDto>(schemaJson, JsonOpts.Default)
                         ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());

            return Results.Ok(new LayerSchemaResponseDto(layerId, r.GetInt32(0), schema));
        });

        return group;
    }
}
