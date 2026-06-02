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
        // hotfix 3件目: クリック位置 (EPSG:3857) に近い feature を返す。
        // WebGIS singleclick → 本 endpoint で entity_id 取得 → WinForms に通知 → 属性表示。
        // tolerance は EPSG:3857 メートル単位 (デフォルト 50m)。
        group.MapGet("/{layerId:int}/at", async (int layerId, double x, double y, double? tolerance,
                                                  NpgsqlDataSource db) =>
        {
            var tol = tolerance ?? 50.0;
            const string sql = @"
                SELECT entity_id, ST_Distance(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857)) AS dist
                  FROM feature_current
                 WHERE layer_id = @id
                   AND geom IS NOT NULL
                   AND ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857), @tol)
                   AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = feature_current.layer_id AND l.deleted_at IS NULL)
                 ORDER BY dist
                 LIMIT 5";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("x", x);
            cmd.Parameters.AddWithValue("y", y);
            cmd.Parameters.AddWithValue("tol", tol);
            await using var r = await cmd.ExecuteReaderAsync();
            var hits = new List<object>();
            while (await r.ReadAsync())
            {
                hits.Add(new
                {
                    entityId = r.GetGuid(0).ToString(),
                    distance = r.GetDouble(1)
                });
            }
            return Results.Ok(new { layerId, hits });
        });

        // hotfix 2件目 (Phase D 朝の動作確認):
        // GET /api/layers/{id}/extent — feature_current の bbox (EPSG:3857) を返す。
        // WebGIS が layer 選択時に view.fit で自動 zoom するために使う。
        group.MapGet("/{layerId:int}/extent", async (int layerId, NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT
                    ST_XMin(ST_Extent(geom)) AS minx,
                    ST_YMin(ST_Extent(geom)) AS miny,
                    ST_XMax(ST_Extent(geom)) AS maxx,
                    ST_YMax(ST_Extent(geom)) AS maxy,
                    COUNT(*) AS feat_count
                FROM feature_current
                WHERE layer_id = @id
                  AND geom IS NOT NULL
                  AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = feature_current.layer_id AND l.deleted_at IS NULL)";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.NotFound();
            if (r.IsDBNull(0)) return Results.Ok(new { layerId, count = 0, extent3857 = (double[]?)null });
            var minx = r.GetDouble(0);
            var miny = r.GetDouble(1);
            var maxx = r.GetDouble(2);
            var maxy = r.GetDouble(3);
            var cnt = r.GetInt64(4);
            return Results.Ok(new
            {
                layerId,
                count = cnt,
                extent3857 = new[] { minx, miny, maxx, maxy }
            });
        });

        // 0205: GET /api/layers — schema_json と schema_version を含めて全レイヤを返す
        // Phase B: deleted_at IS NULL のレイヤのみ (B205 漏れ修正)
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
                       schema_version, schema_json
                FROM layers
                WHERE deleted_at IS NULL
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
                WHERE layer_id = @id AND deleted_at IS NULL";

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
