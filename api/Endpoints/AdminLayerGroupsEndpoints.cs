using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// LG103/LG104 (Phase LG WLG1): レイヤグループ admin CRUD + レイヤ配置。
// 親 MapGroup("/api/admin") の RequireRole("admin") を継承するため、本ファイル内では追加の認可不要。
// adminGroup 直下にマップするため、パスは "/layer-groups..." / "/layers/{layerId}/group" を明示する。
//
// 監査: グループは presentation metadata でバイテンポラル対象外 (PHASE_LG_PLAN.md §1)。
// PL/pgSQL 関数を介さず、各操作の Tx 内で audit_log に直接 INSERT する
// (action='layer_group_create' / 'layer_group_update' / 'layer_group_delete' / 'layer_group_assign')。
// audit_log.layer_id は NULL 許容 (004_audit_log.sql)、actor_user_id は NOT NULL (0A06)。
public static class AdminLayerGroupsEndpoints
{
    public static RouteGroupBuilder MapAdminLayerGroupsEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/admin/layer-groups
        group.MapPost("/layer-groups",
            async (CreateLayerGroupRequestDto req, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            ValidateGroupName(req.GroupName, required: true);

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            if (req.ParentGroupId is int parentId)
            {
                await EnsureGroupExistsAsync(conn, tx, parentId);
            }

            LayerGroupDto dto;
            string afterDoc;
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO layer_group (group_name, parent_group_id, sort_order)
                VALUES (@n, @p, @s)
                RETURNING group_id, parent_group_id, group_name, sort_order,
                          to_jsonb(layer_group.*)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("n", req.GroupName.Trim());
                cmd.Parameters.AddWithValue("p", (object?)req.ParentGroupId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("s", req.SortOrder ?? 0);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                {
                    throw new InvalidOperationException("INSERT RETURNING returned no rows");
                }
                dto = MapGroupDto(r);
                afterDoc = r.GetString(4);
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_create", targetTable: "layer_group",
                layerId: null, beforeDoc: null, afterDoc: afterDoc);

            await tx.CommitAsync();
            return Results.Created($"/api/admin/layer-groups/{dto.GroupId}", dto);
        });

        // PATCH /api/admin/layer-groups/{id}
        // body: { groupName?, parentGroupId?, sortOrder? }
        // parentGroupId は「未指定 = 変更なし」「null = ルート直下へ移動」を区別するため
        // JsonElement で受けて presence を判定する (COALESCE だけでは null 移動が表現できない)。
        group.MapPatch("/layer-groups/{id:int}",
            async (int id, JsonElement body, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("body", "invalid", "request body must be a JSON object")
                });
            }

            string? groupName = null;
            if (body.TryGetProperty("groupName", out var gn))
            {
                if (gn.ValueKind != JsonValueKind.String)
                {
                    throw new ValidationException(new[]
                    {
                        new AttributeErrorDto("groupName", "invalid", "groupName must be a string")
                    });
                }
                groupName = gn.GetString();
                ValidateGroupName(groupName!, required: true);
            }

            var parentProvided = body.TryGetProperty("parentGroupId", out var pg);
            int? parentGroupId = null;
            if (parentProvided && pg.ValueKind != JsonValueKind.Null)
            {
                if (pg.ValueKind != JsonValueKind.Number || !pg.TryGetInt32(out var pv))
                {
                    throw new ValidationException(new[]
                    {
                        new AttributeErrorDto("parentGroupId", "invalid", "parentGroupId must be an integer or null")
                    });
                }
                parentGroupId = pv;
            }

            int? sortOrder = null;
            if (body.TryGetProperty("sortOrder", out var so))
            {
                if (so.ValueKind != JsonValueKind.Number || !so.TryGetInt32(out var sv))
                {
                    throw new ValidationException(new[]
                    {
                        new AttributeErrorDto("sortOrder", "invalid", "sortOrder must be an integer")
                    });
                }
                sortOrder = sv;
            }

            if (groupName is null && !parentProvided && sortOrder is null)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("body", "required",
                        "at least one of groupName/parentGroupId/sortOrder must be provided")
                });
            }

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // before doc 取得 + 行ロック (同一グループへの並行 PATCH を直列化)
            string beforeDoc;
            await using (var cur = new NpgsqlCommand(
                "SELECT to_jsonb(g.*)::text FROM layer_group g WHERE group_id = @id FOR UPDATE", conn, tx))
            {
                cur.Parameters.AddWithValue("id", id);
                beforeDoc = (string?)await cur.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer group not found: {id}");
            }

            if (parentProvided && parentGroupId is int newParent)
            {
                await EnsureGroupExistsAsync(conn, tx, newParent);

                // 循環検証: 新 parent の祖先チェーンを WITH RECURSIVE で走査し、
                // 自分自身 (newParent == id を含む) が現れたら 422 (PHASE_LG_PLAN.md リスク R6)。
                await using var cyc = new NpgsqlCommand(@"
                    WITH RECURSIVE chain AS (
                        SELECT group_id, parent_group_id
                          FROM layer_group
                         WHERE group_id = @newParent
                        UNION ALL
                        SELECT g.group_id, g.parent_group_id
                          FROM layer_group g
                          JOIN chain c ON g.group_id = c.parent_group_id
                    )
                    SELECT 1 FROM chain WHERE group_id = @id LIMIT 1", conn, tx);
                cyc.Parameters.AddWithValue("newParent", newParent);
                cyc.Parameters.AddWithValue("id", id);
                if (await cyc.ExecuteScalarAsync() is not null)
                {
                    throw new ValidationException(new[]
                    {
                        new AttributeErrorDto("parentGroupId", "circular",
                            $"group {id} cannot be moved under itself or its descendant (parentGroupId={newParent})")
                    });
                }
            }

            LayerGroupDto dto;
            string afterDoc;
            await using (var cmd = new NpgsqlCommand(@"
                UPDATE layer_group
                   SET group_name      = COALESCE(@n, group_name),
                       parent_group_id = CASE WHEN @setParent THEN @p::int ELSE parent_group_id END,
                       sort_order      = COALESCE(@s, sort_order),
                       updated_at      = now()
                 WHERE group_id = @id
             RETURNING group_id, parent_group_id, group_name, sort_order,
                       to_jsonb(layer_group.*)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("n", (object?)groupName?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("setParent", parentProvided);
                cmd.Parameters.AddWithValue("p", (object?)parentGroupId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("s", (object?)sortOrder ?? DBNull.Value);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                {
                    throw new NotFoundException($"layer group not found: {id}");
                }
                dto = MapGroupDto(r);
                afterDoc = r.GetString(4);
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_update", targetTable: "layer_group",
                layerId: null, beforeDoc: beforeDoc, afterDoc: afterDoc);

            await tx.CommitAsync();
            return Results.Ok(dto);
        });

        // DELETE /api/admin/layer-groups/{id}
        // 子グループは DB の ON DELETE CASCADE、所属レイヤは ON DELETE SET NULL に任せる。
        // 監査は削除対象の root グループ 1 行のみ記録 (CASCADE で消える子孫は before_doc に含まれない)。
        group.MapDelete("/layer-groups/{id:int}",
            async (int id, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            string beforeDoc;
            await using (var cur = new NpgsqlCommand(
                "SELECT to_jsonb(g.*)::text FROM layer_group g WHERE group_id = @id FOR UPDATE", conn, tx))
            {
                cur.Parameters.AddWithValue("id", id);
                beforeDoc = (string?)await cur.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer group not found: {id}");
            }

            await using (var del = new NpgsqlCommand(
                "DELETE FROM layer_group WHERE group_id = @id", conn, tx))
            {
                del.Parameters.AddWithValue("id", id);
                await del.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_delete", targetTable: "layer_group",
                layerId: null, beforeDoc: beforeDoc, afterDoc: null);

            await tx.CommitAsync();
            return Results.NoContent();
        });

        // LG104: PUT /api/admin/layers/{layerId}/group
        // デフォルトツリーでのレイヤ配置。groupId = null でルート直下。
        group.MapPut("/layers/{layerId:int}/group",
            async (int layerId, AssignLayerGroupRequestDto req, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // before doc (active 行のみ対象): layer 不存在は 404
            string beforeDoc;
            await using (var cur = new NpgsqlCommand(@"
                SELECT jsonb_build_object('layer_id', layer_id, 'group_id', group_id, 'sort_order', sort_order)::text
                  FROM layers
                 WHERE layer_id = @id AND valid_to = '9999-12-31'::date
                   FOR UPDATE", conn, tx))
            {
                cur.Parameters.AddWithValue("id", layerId);
                beforeDoc = (string?)await cur.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer not found: {layerId}");
            }

            if (req.GroupId is int groupId)
            {
                await EnsureGroupExistsAsync(conn, tx, groupId);
            }

            string afterDoc;
            await using (var cmd = new NpgsqlCommand(@"
                UPDATE layers
                   SET group_id   = @g,
                       sort_order = @s,
                       updated_at = now()
                 WHERE layer_id = @id AND valid_to = '9999-12-31'::date
             RETURNING jsonb_build_object('layer_id', layer_id, 'group_id', group_id, 'sort_order', sort_order)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", layerId);
                cmd.Parameters.AddWithValue("g", (object?)req.GroupId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("s", req.SortOrder);
                afterDoc = (string?)await cmd.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer not found: {layerId}");
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_assign", targetTable: "layers",
                layerId: layerId, beforeDoc: beforeDoc, afterDoc: afterDoc);

            await tx.CommitAsync();
            return Results.Ok(new LayerGroupAssignmentDto(layerId, req.GroupId, req.SortOrder));
        });

        return group;
    }

    private static LayerGroupDto MapGroupDto(NpgsqlDataReader r) =>
        new(
            GroupId:       r.GetInt32(0),
            ParentGroupId: r.IsDBNull(1) ? null : r.GetInt32(1),
            GroupName:     r.GetString(2),
            SortOrder:     r.GetInt32(3));

    private static void ValidateGroupName(string? groupName, bool required)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            if (!required) return;
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("groupName", "required", "groupName is required")
            });
        }
        if (groupName.Trim().Length > 100)
        {
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("groupName", "length", "groupName must be between 1 and 100 characters")
            });
        }
    }

    private static async Task EnsureGroupExistsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int groupId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM layer_group WHERE group_id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", groupId);
        if (await cmd.ExecuteScalarAsync() is null)
        {
            throw new NotFoundException($"layer group not found: {groupId}");
        }
    }

    // PL/pgSQL 関数の audit INSERT (0A06) と同じ列構成で C# 側から直接記録する。
    // actor = display_name snapshot, actor_user_id / actor_org_id = ICurrentUser 由来。
    private static async Task InsertAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ICurrentUser user, HttpContext ctx,
        string action, string targetTable, int? layerId, string? beforeDoc, string? afterDoc)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO audit_log (
                actor, actor_user_id, actor_org_id, action, target_table,
                layer_id, entity_id, feature_id,
                before_doc, after_doc, request_id
            ) VALUES (
                @act, @uid, @aoid, @action, @tt,
                @lid, NULL, NULL,
                @before::jsonb, @after::jsonb, @rid
            )", conn, tx);
        cmd.Parameters.AddWithValue("act", user.DisplayName);
        cmd.Parameters.AddWithValue("uid", user.UserId);
        cmd.Parameters.AddWithValue("aoid", user.OrgId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("tt", targetTable);
        cmd.Parameters.AddWithValue("lid", (object?)layerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("before", (object?)beforeDoc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("after", (object?)afterDoc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rid", RequestContext.GetRequestId(ctx));
        await cmd.ExecuteNonQueryAsync();
    }
}
