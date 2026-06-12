using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Services;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// F203 (Phase F WF2): 組織×レイヤ権限の管理 endpoint。
// `/api/admin/organizations/{orgId}/layer-permissions` 配下に GET / PUT を生やす。
// 親 MapGroup("/api/admin") で RequireRole("admin") を継承するため、本ファイル内では追加の認可不要。
public static class AdminOrgLayerPermissionsEndpoints
{
    public static RouteGroupBuilder MapAdminOrgLayerPermissionsEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/organizations/{orgId}/layer-permissions
        // 該当 org の全 active layer について (canView, canEdit) を返す。
        // org_layer_permission に行が無い layer は can_view=false / can_edit=false で返す
        // (未設定 = アクセス不可、明示的な行が無くても返却)。
        group.MapGet("/{orgId:int}/layer-permissions", async (int orgId, NpgsqlDataSource db) =>
        {
            await using (var oc = db.CreateCommand(
                "SELECT 1 FROM organizations WHERE id = @id AND deleted_at IS NULL"))
            {
                oc.Parameters.AddWithValue("id", orgId);
                if (await oc.ExecuteScalarAsync() is null)
                    throw new NotFoundException($"organization not found: {orgId}");
            }

            const string sql = @"
                SELECT l.layer_id, l.layer_name, l.layer_type,
                       COALESCE(p.can_view, false) AS can_view,
                       COALESCE(p.can_edit, false) AS can_edit
                  FROM layers l
                  LEFT JOIN org_layer_permission p
                    ON p.layer_id = l.layer_id AND p.org_id = @oid
                 WHERE l.valid_to = '9999-12-31'::date
                 ORDER BY l.layer_id";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("oid", orgId);
            await using var r = await cmd.ExecuteReaderAsync();

            var list = new List<OrgLayerPermissionDto>();
            while (await r.ReadAsync())
            {
                list.Add(new OrgLayerPermissionDto(
                    OrgId: orgId,
                    LayerId: r.GetInt32(0),
                    LayerName: r.GetString(1),
                    LayerType: r.GetString(2),
                    CanView: r.GetBoolean(3),
                    CanEdit: r.GetBoolean(4)));
            }
            return Results.Ok(list);
        });

        // PUT /api/admin/organizations/{orgId}/layer-permissions
        // permissions の全件を fn_org_layer_perm_upsert で 1 トランザクションで反映。
        // 部分更新 (送られた layerId のみ) は許容、送られなかった layer は変更されない。
        group.MapPut("/{orgId:int}/layer-permissions",
            async (int orgId, OrgLayerPermsUpsertDto req, HttpContext ctx,
                   ICurrentUser user, NpgsqlDataSource db,
                   ILayerInvalidationBroker broker) =>
        {
            // 入力検証
            var errs = new List<AttributeErrorDto>();
            if (req.Permissions is null || req.Permissions.Count == 0)
            {
                errs.Add(new AttributeErrorDto("permissions", "required",
                    "permissions must be non-empty"));
            }
            else
            {
                for (var i = 0; i < req.Permissions.Count; i++)
                {
                    var p = req.Permissions[i];
                    if (p.CanEdit && !p.CanView)
                    {
                        errs.Add(new AttributeErrorDto($"permissions[{i}]", "invariant",
                            "can_edit requires can_view"));
                    }
                }
            }
            if (errs.Count > 0) throw new ValidationException(errs);

            await using (var oc = db.CreateCommand(
                "SELECT 1 FROM organizations WHERE id = @id AND deleted_at IS NULL"))
            {
                oc.Parameters.AddWithValue("id", orgId);
                if (await oc.ExecuteScalarAsync() is null)
                    throw new NotFoundException($"organization not found: {orgId}");
            }

            var actor = user.DisplayName;
            var rid = RequestContext.GetRequestId(ctx);

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var p in req.Permissions!)
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT fn_org_layer_perm_upsert(@oid, @lid, @cv, @ce, @act, @rid, @uid, @aoid)",
                    conn, tx);
                cmd.Parameters.AddWithValue("oid", orgId);
                cmd.Parameters.AddWithValue("lid", p.LayerId);
                cmd.Parameters.AddWithValue("cv", p.CanView);
                cmd.Parameters.AddWithValue("ce", p.CanEdit);
                cmd.Parameters.AddWithValue("act", actor);
                cmd.Parameters.AddWithValue("rid", rid);
                cmd.Parameters.AddWithValue("uid", user.UserId);
                cmd.Parameters.AddWithValue("aoid", user.OrgId);

                try
                {
                    await cmd.ExecuteScalarAsync();
                }
                catch (PostgresException pe) when (pe.SqlState == "23503")
                {
                    // layer_id への FK 違反 (存在しない layer_id)
                    throw new NotFoundException($"layer not found: {p.LayerId}");
                }
            }

            await tx.CommitAsync();

            // F'401 (Phase F' WF'4): tx commit 後に broker.PublishPermissionInvalidate を fire。
            // 該当 org に所属する user の WebGIS SSE に届き、tile cache 即時 flush + fetchLayers 再取得が走る。
            // 失敗しても endpoint 自体は成功扱い (broker は best-effort)。
            try
            {
                var changedLayerIds = req.Permissions!.Select(p => p.LayerId).Distinct().ToList();
                broker.PublishPermissionInvalidate(orgId, changedLayerIds);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AdminOrgLayerPermissionsEndpoints] broker.Publish failed: {ex.Message}");
            }

            // 更新後の全権限を再取得して返す
            const string returnSql = @"
                SELECT l.layer_id, l.layer_name, l.layer_type,
                       COALESCE(p.can_view, false) AS can_view,
                       COALESCE(p.can_edit, false) AS can_edit
                  FROM layers l
                  LEFT JOIN org_layer_permission p
                    ON p.layer_id = l.layer_id AND p.org_id = @oid
                 WHERE l.valid_to = '9999-12-31'::date
                 ORDER BY l.layer_id";
            await using var rcmd = db.CreateCommand(returnSql);
            rcmd.Parameters.AddWithValue("oid", orgId);
            await using var r = await rcmd.ExecuteReaderAsync();
            var list = new List<OrgLayerPermissionDto>();
            while (await r.ReadAsync())
            {
                list.Add(new OrgLayerPermissionDto(
                    OrgId: orgId,
                    LayerId: r.GetInt32(0),
                    LayerName: r.GetString(1),
                    LayerType: r.GetString(2),
                    CanView: r.GetBoolean(3),
                    CanEdit: r.GetBoolean(4)));
            }
            return Results.Ok(list);
        });

        return group;
    }
}
