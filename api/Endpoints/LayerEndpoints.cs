using System.Text.Json;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using AgriGis.Api.Shared;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class LayerEndpoints
{
    public static RouteGroupBuilder MapLayerEndpoints(this RouteGroupBuilder group)
    {
        // hotfix 3件目: クリック位置 (EPSG:3857) に近い feature を返す。
        // WebGIS singleclick → 本 endpoint で entity_id 取得 → WinForms に通知 → 属性表示。
        // tolerance は EPSG:3857 メートル単位 (デフォルト 50m)。
        group.MapGet("/{layerId:int}/at", async (int layerId, double x, double y, double? tolerance, string? asOf,
                                                  NpgsqlDataSource db) =>
        {
            var tol = tolerance ?? 50.0;
            var asOfDate = AsOfParser.TryParse(asOf);
            // E204 (WE2): asOf あり時は feature_asof view 経由で valid_from/_to を絞る
            string sql;
            if (asOfDate is null)
            {
                sql = @"
                    SELECT entity_id, ST_Distance(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857)) AS dist
                      FROM feature_current
                     WHERE layer_id = @id
                       AND geom IS NOT NULL
                       AND ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857), @tol)
                       AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = feature_current.layer_id AND l.deleted_at IS NULL)
                     ORDER BY dist
                     LIMIT 5";
            }
            else
            {
                sql = @"
                    SELECT entity_id, ST_Distance(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857)) AS dist
                      FROM feature_asof
                     WHERE layer_id = @id
                       AND geom IS NOT NULL
                       AND valid_from <= @asof AND @asof < valid_to
                       AND ST_DWithin(geom, ST_SetSRID(ST_MakePoint(@x, @y), 3857), @tol)
                     ORDER BY dist
                     LIMIT 5";
            }
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("x", x);
            cmd.Parameters.AddWithValue("y", y);
            cmd.Parameters.AddWithValue("tol", tol);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
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
        group.MapGet("/{layerId:int}/extent", async (int layerId, string? asOf, NpgsqlDataSource db) =>
        {
            var asOfDate = AsOfParser.TryParse(asOf);
            // E204 (WE2): asOf あり時は feature_asof view 経由
            string sql;
            if (asOfDate is null)
            {
                sql = @"
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
            }
            else
            {
                sql = @"
                    SELECT
                        ST_XMin(ST_Extent(geom)) AS minx,
                        ST_YMin(ST_Extent(geom)) AS miny,
                        ST_XMax(ST_Extent(geom)) AS maxx,
                        ST_YMax(ST_Extent(geom)) AS maxy,
                        COUNT(*) AS feat_count
                    FROM feature_asof
                    WHERE layer_id = @id
                      AND geom IS NOT NULL
                      AND valid_from <= @asof AND @asof < valid_to";
            }
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
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

        // 0205 + E201 (WE2): GET /api/layers?asOf=YYYY-MM-DD
        // asOf 無し: layers の active 行のみ (valid_to='9999-12-31')
        // asOf あり: layers + layer_history UNION ALL で valid_from <= asOf < valid_to を絞る
        group.MapGet("/", async (string? asOf, NpgsqlDataSource db) =>
        {
            var asOfDate = AsOfParser.TryParse(asOf);
            string sql;
            if (asOfDate is null)
            {
                sql = @"
                    SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
                           schema_version, schema_json
                      FROM layers
                     WHERE valid_to = '9999-12-31'::date
                       AND deleted_at IS NULL
                     ORDER BY layer_id";
            }
            else
            {
                sql = @"
                    SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
                           schema_version, schema_json
                      FROM layers
                     WHERE valid_from <= @asof AND @asof < valid_to
                    UNION ALL
                    SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
                           schema_version, schema_json
                      FROM layer_history
                     WHERE valid_from <= @asof AND @asof < valid_to
                     ORDER BY layer_id";
            }

            await using var cmd = db.CreateCommand(sql);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
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

        // 0206 + E201 (WE2): GET /api/layers/{layerId}/schema?asOf=
        group.MapGet("/{layerId:int}/schema", async (int layerId, string? asOf, NpgsqlDataSource db) =>
        {
            var asOfDate = AsOfParser.TryParse(asOf);
            string sql;
            if (asOfDate is null)
            {
                sql = @"
                    SELECT schema_version, schema_json
                      FROM layers
                     WHERE layer_id = @id AND valid_to = '9999-12-31'::date AND deleted_at IS NULL";
            }
            else
            {
                // asOf 時点の schema は layer_schema_version の同 layer の有効バージョン
                sql = @"
                    SELECT schema_version, schema_json
                      FROM layer_schema_version
                     WHERE layer_id = @id
                       AND valid_from <= @asof
                       AND (valid_to IS NULL OR @asof < valid_to::date)
                     ORDER BY valid_from DESC
                     LIMIT 1";
            }

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
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
