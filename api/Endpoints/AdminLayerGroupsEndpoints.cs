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

            // LGP103: parent は自 org の group のみ許可 (他 org の group を親にできない → 404)
            if (req.ParentGroupId is int parentId)
            {
                await EnsureGroupExistsAsync(conn, tx, parentId, user.OrgId);
            }

            LayerGroupDto dto;
            string afterDoc;
            // LGP103: org_id = actor.OrgId を強制 (body で org 指定はさせない)
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO layer_group (group_name, parent_group_id, sort_order, org_id)
                VALUES (@n, @p, @s, @org)
                RETURNING group_id, parent_group_id, group_name, sort_order,
                          to_jsonb(layer_group.*)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("n", req.GroupName.Trim());
                cmd.Parameters.AddWithValue("p", (object?)req.ParentGroupId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("s", req.SortOrder ?? 0);
                cmd.Parameters.AddWithValue("org", user.OrgId);
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
            // LGP103: 自 org の group のみ対象 (他 org の group は 0 件 = 404 で越権を遮断)
            string beforeDoc;
            await using (var cur = new NpgsqlCommand(
                "SELECT to_jsonb(g.*)::text FROM layer_group g WHERE group_id = @id AND org_id = @org FOR UPDATE", conn, tx))
            {
                cur.Parameters.AddWithValue("id", id);
                cur.Parameters.AddWithValue("org", user.OrgId);
                beforeDoc = (string?)await cur.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer group not found: {id}");
            }

            if (parentProvided && parentGroupId is int newParent)
            {
                // LGP103: 新 parent も自 org の group であることを保証 (他 org parent → 404)
                await EnsureGroupExistsAsync(conn, tx, newParent, user.OrgId);

                // 循環検証: 新 parent の祖先チェーンを WITH RECURSIVE で走査し、
                // 自分自身 (newParent == id を含む) が現れたら 422 (PHASE_LG_PLAN.md リスク R6)。
                // LGP103: chain も自 org 内に限定 (parent も同 org が保証済なので org フィルタを付ける)。
                await using var cyc = new NpgsqlCommand(@"
                    WITH RECURSIVE chain AS (
                        SELECT group_id, parent_group_id
                          FROM layer_group
                         WHERE group_id = @newParent AND org_id = @org
                        UNION ALL
                        SELECT g.group_id, g.parent_group_id
                          FROM layer_group g
                          JOIN chain c ON g.group_id = c.parent_group_id
                         WHERE g.org_id = @org
                    )
                    SELECT 1 FROM chain WHERE group_id = @id LIMIT 1", conn, tx);
                cyc.Parameters.AddWithValue("newParent", newParent);
                cyc.Parameters.AddWithValue("id", id);
                cyc.Parameters.AddWithValue("org", user.OrgId);
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
                 WHERE group_id = @id AND org_id = @org
             RETURNING group_id, parent_group_id, group_name, sort_order,
                       to_jsonb(layer_group.*)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("org", user.OrgId);
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

            // LGP103: 自 org の group のみ削除可 (他 org は 0 件 = 404)
            string beforeDoc;
            await using (var cur = new NpgsqlCommand(
                "SELECT to_jsonb(g.*)::text FROM layer_group g WHERE group_id = @id AND org_id = @org FOR UPDATE", conn, tx))
            {
                cur.Parameters.AddWithValue("id", id);
                cur.Parameters.AddWithValue("org", user.OrgId);
                beforeDoc = (string?)await cur.ExecuteScalarAsync()
                    ?? throw new NotFoundException($"layer group not found: {id}");
            }

            await using (var del = new NpgsqlCommand(
                "DELETE FROM layer_group WHERE group_id = @id AND org_id = @org", conn, tx))
            {
                del.Parameters.AddWithValue("id", id);
                del.Parameters.AddWithValue("org", user.OrgId);
                await del.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_delete", targetTable: "layer_group",
                layerId: null, beforeDoc: beforeDoc, afterDoc: null);

            await tx.CommitAsync();
            return Results.NoContent();
        });

        // LG104 / LGP104: PUT /api/admin/layers/{layerId}/group
        // デフォルトツリーでのレイヤ配置。groupId = null でルート直下。
        // LGP104: layers.group_id 直更新を廃止し、layer_group_member (org_id, layer_id) を upsert する。
        //   - layer は active かつ自 org が閲覧可 (org_layer_permission.can_view) であること。
        //     不存在/閲覧不可 → 404 (admin であっても自 org のツリーのみ管理する)
        //   - groupId は自 org の group のみ許可 (他 org の group → 404)
        group.MapPut("/layers/{layerId:int}/group",
            async (int layerId, AssignLayerGroupRequestDto req, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            var orgId = user.OrgId;

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // layer が active であること (不存在 → 404)。閲覧可否は org_layer_permission で判定。
            await using (var cur = new NpgsqlCommand(@"
                SELECT 1 FROM layers
                 WHERE layer_id = @id AND valid_to = '9999-12-31'::date", conn, tx))
            {
                cur.Parameters.AddWithValue("id", layerId);
                if (await cur.ExecuteScalarAsync() is null)
                    throw new NotFoundException($"layer not found: {layerId}");
            }

            // 自 org が当該 layer を閲覧可であること (can_view=true)。閲覧不可 → 404。
            await using (var perm = new NpgsqlCommand(@"
                SELECT 1 FROM org_layer_permission
                 WHERE org_id = @org AND layer_id = @id AND can_view = true", conn, tx))
            {
                perm.Parameters.AddWithValue("org", orgId);
                perm.Parameters.AddWithValue("id", layerId);
                if (await perm.ExecuteScalarAsync() is null)
                    throw new NotFoundException($"layer not found: {layerId}");
            }

            if (req.GroupId is int groupId)
            {
                // LGP104: 自 org の group のみ配置先に許可 (他 org の group → 404)
                await EnsureGroupExistsAsync(conn, tx, groupId, orgId);
            }

            // before doc: 既存 member 行 (無ければ null)。audit の before/after に使う。
            string? beforeDoc;
            await using (var bc = new NpgsqlCommand(@"
                SELECT jsonb_build_object('layer_id', layer_id, 'group_id', group_id, 'sort_order', sort_order)::text
                  FROM layer_group_member
                 WHERE org_id = @org AND layer_id = @id
                   FOR UPDATE", conn, tx))
            {
                bc.Parameters.AddWithValue("org", orgId);
                bc.Parameters.AddWithValue("id", layerId);
                beforeDoc = (string?)await bc.ExecuteScalarAsync();
            }

            string afterDoc;
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO layer_group_member (org_id, layer_id, group_id, sort_order)
                VALUES (@org, @id, @g, @s)
                ON CONFLICT (org_id, layer_id)
                DO UPDATE SET group_id = EXCLUDED.group_id, sort_order = EXCLUDED.sort_order
             RETURNING jsonb_build_object('layer_id', layer_id, 'group_id', group_id, 'sort_order', sort_order)::text", conn, tx))
            {
                cmd.Parameters.AddWithValue("org", orgId);
                cmd.Parameters.AddWithValue("id", layerId);
                cmd.Parameters.AddWithValue("g", (object?)req.GroupId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("s", req.SortOrder);
                afterDoc = (string)(await cmd.ExecuteScalarAsync())!;
            }

            await InsertAuditAsync(conn, tx, user, ctx,
                action: "layer_group_assign", targetTable: "layer_group_member",
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

    // LGP103: group は自 org のもののみ「存在」とみなす (他 org の group は 404)。
    private static async Task EnsureGroupExistsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, int groupId, int orgId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM layer_group WHERE group_id = @id AND org_id = @org", conn, tx);
        cmd.Parameters.AddWithValue("id", groupId);
        cmd.Parameters.AddWithValue("org", orgId);
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
