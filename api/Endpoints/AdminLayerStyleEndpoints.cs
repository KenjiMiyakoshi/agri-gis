using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Style;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// D203 (WD2): admin theme CRUD
//   GET /api/admin/layers/{id}/style
//   PUT /api/admin/layers/{id}/style
// 認可: admin (group 経由で gate 済)
// PUT 時に theme ごとに SldXmlBuilder で SLD XML 生成 → IGeoServerStyleSync.PushStyleAsync
// GeoServer 同期失敗時は DB transaction を rollback して 500
public static class AdminLayerStyleEndpoints
{
    public static RouteGroupBuilder MapAdminLayerStyleEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/layers/{id}/style
        group.MapGet("/{id:int}/style", async (int id,
                                                NpgsqlDataSource db,
                                                CancellationToken ct) =>
        {
            const string sql = @"
                SELECT style_json
                  FROM layers
                 WHERE layer_id = @id AND deleted_at IS NULL";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return Results.NotFound();
            }
            var json = r.GetFieldValue<string>(0);  // Npgsql は JSONB を string で取得可能
            using var doc = JsonDocument.Parse(json);
            return Results.Ok(new LayerStyleDto(doc.RootElement.Clone()));
        });

        // PUT /api/admin/layers/{id}/style
        group.MapPut("/{id:int}/style", async (int id,
                                                LayerStyleDto req,
                                                NpgsqlDataSource db,
                                                IGeoServerStyleSync sync,
                                                CancellationToken ct) =>
        {
            // 既存 layer 確認
            const string existsSql = @"
                SELECT 1
                  FROM layers
                 WHERE layer_id = @id AND deleted_at IS NULL";
            await using (var existsCmd = db.CreateCommand(existsSql))
            {
                existsCmd.Parameters.AddWithValue("id", id);
                var found = await existsCmd.ExecuteScalarAsync(ct);
                if (found is null) return Results.NotFound();
            }

            // themes キーを取り出して各 theme 名を validation
            if (req.StyleJson.ValueKind != JsonValueKind.Object ||
                !req.StyleJson.TryGetProperty("themes", out var themesElem) ||
                themesElem.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("themes", "required",
                        "style_json must be an object with 'themes' key holding theme→config map")
                });
            }

            // DB transaction で UPDATE → 各 theme の SLD を GeoServer に POST
            // GeoServer 失敗時は rollback。すべての theme を順次 push、1 つでも失敗で全 rollback。
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            await using (var upd = new NpgsqlCommand(
                "UPDATE layers SET style_json = @j::jsonb, updated_at = now() WHERE layer_id = @id", conn))
            {
                upd.Transaction = tx;
                upd.Parameters.AddWithValue("j", req.StyleJson.GetRawText());
                upd.Parameters.AddWithValue("id", id);
                await upd.ExecuteNonQueryAsync(ct);
            }

            foreach (var theme in themesElem.EnumerateObject())
            {
                var themeName = theme.Name;
                if (!System.Text.RegularExpressions.Regex.IsMatch(themeName, @"^[a-z0-9_]{1,32}$"))
                {
                    await tx.RollbackAsync(ct);
                    throw new ValidationException(new[]
                    {
                        new AttributeErrorDto($"themes.{themeName}", "pattern",
                            $"theme name must match ^[a-z0-9_]{{1,32}}$")
                    });
                }
                var sld = SldXmlBuilder.Build(themeName, theme.Value);
                var pushed = await sync.PushStyleAsync(id, themeName, sld, ct);
                if (!pushed)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Problem(
                        title: "GeoServer style sync failed",
                        detail: $"theme '{themeName}' could not be pushed to GeoServer; DB changes rolled back",
                        statusCode: 502);
                }
            }

            await tx.CommitAsync(ct);
            return Results.Ok(req);
        });

        return group;
    }
}
