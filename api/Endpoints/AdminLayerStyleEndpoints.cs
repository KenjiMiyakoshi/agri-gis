using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Shared;
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
        // GET /api/admin/layers/{id}/style?asOf=YYYY-MM-DD
        // E203 (WE2): asOf 指定で layer_style_version の過去版を引く
        group.MapGet("/{id:int}/style", async (int id, string? asOf,
                                                NpgsqlDataSource db,
                                                CancellationToken ct) =>
        {
            var asOfDate = AsOfParser.TryParse(asOf);
            string sql;
            if (asOfDate is null)
            {
                sql = @"
                    SELECT style_json
                      FROM layers
                     WHERE layer_id = @id AND deleted_at IS NULL";
            }
            else
            {
                sql = @"
                    SELECT style_json
                      FROM layer_style_version
                     WHERE layer_id = @id
                       AND valid_from <= @asof AND @asof < valid_to
                     ORDER BY style_version DESC
                     LIMIT 1";
            }
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", id);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return Results.NotFound();
            }
            var json = r.GetFieldValue<string>(0);
            using var doc = JsonDocument.Parse(json);
            return Results.Ok(new LayerStyleDto(doc.RootElement.Clone()));
        });

        // PUT /api/admin/layers/{id}/style — E203 (WE2): fn_layer_style_upsert 経由化
        group.MapPut("/{id:int}/style", async (int id,
                                                LayerStyleDto req,
                                                NpgsqlDataSource db,
                                                IGeoServerStyleSync sync,
                                                ICurrentUser currentUser,
                                                HttpContext ctx,
                                                CancellationToken ct) =>
        {
            var requestId = RequestContext.GetRequestId(ctx);
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

            // DB transaction で fn_layer_style_upsert → 各 theme の SLD を GeoServer に POST
            // GeoServer 失敗時は rollback。すべての theme を順次 push、1 つでも失敗で全 rollback。
            // E203 (WE2): fn_layer_style_upsert 経由化 (layer_style_version への履歴 append + layers.style_json 同期)
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            await using (var upd = new NpgsqlCommand(@"
                SELECT * FROM fn_layer_style_upsert(
                    p_layer_id => @id,
                    p_style_json => @j::jsonb,
                    p_actor => @act,
                    p_request_id => @rid,
                    p_user_id => @uid,
                    p_org_id => @oid)", conn))
            {
                upd.Transaction = tx;
                upd.Parameters.AddWithValue("id", id);
                upd.Parameters.AddWithValue("j", req.StyleJson.GetRawText());
                upd.Parameters.AddWithValue("act", currentUser.DisplayName);
                upd.Parameters.AddWithValue("rid", requestId);
                upd.Parameters.AddWithValue("uid", currentUser.UserId);
                upd.Parameters.AddWithValue("oid", currentUser.OrgId);
                try
                {
                    await upd.ExecuteScalarAsync(ct);
                }
                catch (PostgresException pe) when (pe.SqlState == "02000")
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound();
                }
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
