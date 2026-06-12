using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using AgriGis.Api.Validation;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class FeatureEndpoints
{
    // WB2 B201 (H2 解消): api/Json/JsonOpts.Default に集約済み

    public static RouteGroupBuilder MapFeatureEndpoints(this RouteGroupBuilder group)
    {
        // 0208 + D205 (WD2) + D303 (WD3): GET /api/features?layerId= は **410 Gone**。
        // Phase D 採用案 P §2.2: WD3 完了時点で完全廃止。
        // 後継は `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` (D201)。
        // 単一 entity 取得は `/api/features/{entityId}` (既存) を継続。
        group.MapGet("/", (HttpContext httpContext) =>
        {
            httpContext.Response.Headers["Link"] =
                "</tiles/{layerId}/{theme}/{z}/{x}/{y}.png>; rel=\"successor-version\"";
            return Results.Problem(
                type: "https://docs.agri-gis/errors/endpoint-gone",
                title: "GET /api/features?layerId= is permanently gone (Phase D)",
                detail: "Replaced by GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png. " +
                        "For single entity access use GET /api/features/{entityId}.",
                statusCode: 410);
        });

        // 0209: GET /api/features/{entityId}?asOf=YYYY-MM-DD
        group.MapGet("/{entityId:guid}", async (Guid entityId, string? asOf, NpgsqlDataSource db) =>
        {
            var asOfDate = ParseAsOf(asOf);

            // WB2 B205: layers.deleted_at IS NULL の対象 entity のみ返却
            string sql = asOfDate is null
                ? @"SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                           attributes_schema_version, created_by, updated_by, created_at, updated_at,
                           attributes, ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj
                      FROM feature_current fc
                     WHERE fc.entity_id = @e AND fc.geom IS NOT NULL
                       AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = fc.layer_id AND l.valid_to = '9999-12-31'::date)"
                : @"WITH unioned AS (
                        SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                               attributes_schema_version, created_by, updated_by, created_at, updated_at,
                               attributes, geom
                          FROM feature_current fc
                         WHERE fc.entity_id = @e AND fc.geom IS NOT NULL
                           AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = fc.layer_id AND l.valid_to = '9999-12-31'::date)
                           AND valid_from <= @asof AND @asof < valid_to
                        UNION ALL
                        SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                               attributes_schema_version, created_by, updated_by, created_at, updated_at,
                               attributes, geom
                          FROM feature_history fh
                         WHERE fh.entity_id = @e AND fh.geom IS NOT NULL
                           AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = fh.layer_id AND l.valid_to = '9999-12-31'::date)
                           AND valid_from <= @asof AND @asof < valid_to
                    )
                    SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                           attributes_schema_version, created_by, updated_by, created_at, updated_at,
                           attributes, ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj
                      FROM unioned
                      LIMIT 1";

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("e", entityId);
            if (asOfDate is not null)
            {
                cmd.Parameters.AddWithValue("asof", asOfDate.Value);
            }

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                throw new NotFoundException($"entity not found: {entityId}");
            }
            return Results.Ok(MapFeatureDto(r));
        });

        // 0209: GET /api/features/{entityId}/history
        group.MapGet("/{entityId:guid}/history", async (Guid entityId, NpgsqlDataSource db) =>
        {
            // WB2 B205: layers.deleted_at IS NULL の対象のみ
            const string sql = @"
                SELECT history_id, feature_id, layer_id, entity_id, version,
                       valid_from, valid_to, attributes_schema_version,
                       created_by, updated_by, created_at, updated_at,
                       archived_at, archived_by, archived_reason,
                       ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj,
                       attributes
                  FROM feature_history fh
                 WHERE fh.entity_id = @e
                   AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = fh.layer_id AND l.valid_to = '9999-12-31'::date)
                 ORDER BY valid_to DESC, history_id DESC";

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("e", entityId);

            await using var r = await cmd.ExecuteReaderAsync();
            var history = new List<FeatureHistoryDto>();
            while (await r.ReadAsync())
            {
                history.Add(MapFeatureHistoryDto(r));
            }
            return Results.Ok(history);
        });

        // 0210: POST /api/features
        // F204 (Phase F WF2): org_layer_permission.can_edit 検査を追加。
        // admin role は service 内で bypass、それ以外は false なら 403。
        group.MapPost("/", async (CreateFeatureRequestDto req, HttpContext ctx, ICurrentUser user,
                                   ILayerPermissionService perm, NpgsqlDataSource db, CancellationToken ct) =>
        {
            if (!await perm.CanEditAsync(user.OrgId, req.LayerId, user.Roles, ct))
            {
                return Results.Problem(
                    type: "https://docs.agri-gis/errors/layer-permission-denied",
                    title: "Edit denied for this layer",
                    detail: $"Your organization does not have can_edit for layer {req.LayerId}.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var actor = user.DisplayName;
            var rid = RequestContext.GetRequestId(ctx);

            // 該当レイヤの現行 schema を取得
            LayerSchemaDto schema;
            int schemaVersion;
            await using (var c = db.CreateCommand(
                "SELECT schema_version, schema_json FROM layers WHERE layer_id = @id AND valid_to = '9999-12-31'::date"))
            {
                c.Parameters.AddWithValue("id", req.LayerId);
                await using var rr = await c.ExecuteReaderAsync();
                if (!await rr.ReadAsync())
                {
                    throw new NotFoundException($"layer not found: {req.LayerId}");
                }
                schemaVersion = rr.GetInt32(0);
                schema = JsonSerializer.Deserialize<LayerSchemaDto>(rr.GetString(1), JsonOpts.Default)
                         ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());
            }

            var attrs = req.Attributes ?? new Dictionary<string, JsonElement>();
            var errs = AttributeValidator.Validate(schema, attrs);
            if (errs.Count > 0)
            {
                throw new ValidationException(errs);
            }

            var entityId = Guid.NewGuid();
            await using var cmd = db.CreateCommand(
                "SELECT fn_feature_insert(@l, @e, @g, @a::jsonb, @act, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("l", req.LayerId);
            cmd.Parameters.AddWithValue("e", entityId);
            cmd.Parameters.AddWithValue("g", req.Geometry.GetRawText());
            cmd.Parameters.AddWithValue("a", JsonSerializer.Serialize(attrs, JsonOpts.Default));
            cmd.Parameters.AddWithValue("act", actor);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            var featureId = (long)(await cmd.ExecuteScalarAsync())!;

            return Results.Created($"/api/features/{entityId}",
                new
                {
                    featureId,
                    entityId,
                    version = 1,
                    attributesSchemaVersion = schemaVersion
                });
        }).RequireAuthorization("WriteFeature");

        // 0211: PATCH /api/features/{entityId} (If-Match 必須、楽観ロック)
        // F204 (Phase F WF2): entity 所属 layer の can_edit を検査。
        group.MapPatch("/{entityId:guid}",
            async (Guid entityId, UpdateFeatureRequestDto req, HttpContext ctx, ICurrentUser user,
                   ILayerPermissionService perm, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var actor = user.DisplayName;
            var rid = RequestContext.GetRequestId(ctx);

            var ifMatch = ctx.Request.Headers["If-Match"].ToString();
            if (!int.TryParse(ifMatch, out var expected))
            {
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            }

            // 現行 layer の schema + layer_id を取得（属性バリデーション + can_edit 用）
            LayerSchemaDto schema;
            int entityLayerId;
            await using (var c = db.CreateCommand(
                @"SELECT l.schema_json, l.layer_id
                    FROM feature_current fc
                    JOIN layers l ON l.layer_id = fc.layer_id
                   WHERE fc.entity_id = @e AND l.valid_to = '9999-12-31'::date"))
            {
                c.Parameters.AddWithValue("e", entityId);
                await using var rr = await c.ExecuteReaderAsync();
                if (!await rr.ReadAsync())
                {
                    throw new NotFoundException($"entity not found: {entityId}");
                }
                schema = JsonSerializer.Deserialize<LayerSchemaDto>(rr.GetString(0), JsonOpts.Default)
                         ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());
                entityLayerId = rr.GetInt32(1);
            }

            if (!await perm.CanEditAsync(user.OrgId, entityLayerId, user.Roles, ct))
            {
                return Results.Problem(
                    type: "https://docs.agri-gis/errors/layer-permission-denied",
                    title: "Edit denied for this layer",
                    detail: $"Your organization does not have can_edit for layer {entityLayerId}.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (req.Attributes is { } attrs)
            {
                var errs = AttributeValidator.Validate(schema, attrs);
                if (errs.Count > 0)
                {
                    throw new ValidationException(errs);
                }
            }

            await using var cmd = db.CreateCommand(
                @"SELECT fn_feature_update(@e, @g, @a::jsonb, @act, @ev, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("e", entityId);
            cmd.Parameters.AddWithValue("g",
                req.Geometry?.GetRawText() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("a",
                req.Attributes is null
                    ? (object)DBNull.Value
                    : JsonSerializer.Serialize(req.Attributes, JsonOpts.Default));
            cmd.Parameters.AddWithValue("act", actor);
            cmd.Parameters.AddWithValue("ev", expected);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            var newVersion = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Ok(new { entityId, version = newVersion });
        }).RequireAuthorization("WriteFeature");

        // D'104 (WD'1): POST /api/features/batch — N 件まとめて属性 patch (all-or-nothing)
        // 楽観ロック (entityIds × ifMatchVersions の全件突合)、1 件 mismatch → 409 + 全件 rollback。
        // geometry は不変、属性のみ JSONB merge (`||`) で patch。
        // F204 (Phase F WF2): entityIds が跨る全 layer について can_edit を検査 (1件でも false なら 403)。
        group.MapPost("/batch", async (
            FeatureBatchUpdateRequestDto req, HttpContext ctx, ICurrentUser user,
            ILayerPermissionService perm, NpgsqlDataSource db, CancellationToken ct) =>
        {
            // 入力検証
            var errs = new List<AttributeErrorDto>();
            if (req.EntityIds is null || req.EntityIds.Count == 0)
                errs.Add(new AttributeErrorDto("entityIds", "required", "entityIds must be non-empty"));
            if (req.IfMatchVersions is null || req.IfMatchVersions.Count == 0)
                errs.Add(new AttributeErrorDto("ifMatchVersions", "required", "ifMatchVersions must be non-empty"));
            if (req.EntityIds is not null && req.IfMatchVersions is not null
                && req.EntityIds.Count != req.IfMatchVersions.Count)
                errs.Add(new AttributeErrorDto("ifMatchVersions", "length_mismatch",
                    "entityIds and ifMatchVersions must have same length"));
            if (req.EntityIds is not null && req.EntityIds.Count > 1000)
                errs.Add(new AttributeErrorDto("entityIds", "limit",
                    "entityIds limit is 1000 per batch"));
            if (errs.Count > 0) throw new ValidationException(errs);

            // F204: 対象 entity が属する全 layer を取得し、can_edit を検査
            // admin role 持ちは service 内で bypass されるので DB query 不要
            if (!user.HasRole("admin"))
            {
                await using var lcmd = db.CreateCommand(
                    @"SELECT DISTINCT layer_id FROM feature_current
                       WHERE entity_id = ANY(@eids)
                         AND EXISTS (SELECT 1 FROM layers l WHERE l.layer_id = feature_current.layer_id
                                                              AND l.valid_to = '9999-12-31'::date)");
                lcmd.Parameters.AddWithValue("eids", req.EntityIds!.ToArray());
                var layerIds = new List<int>();
                await using (var lr = await lcmd.ExecuteReaderAsync(ct))
                {
                    while (await lr.ReadAsync(ct)) layerIds.Add(lr.GetInt32(0));
                }
                foreach (var lid in layerIds)
                {
                    if (!await perm.CanEditAsync(user.OrgId, lid, user.Roles, ct))
                    {
                        return Results.Problem(
                            type: "https://docs.agri-gis/errors/layer-permission-denied",
                            title: "Edit denied for one or more layers in batch",
                            detail: $"Your organization does not have can_edit for layer {lid}.",
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            var actor = user.DisplayName;
            var rid = RequestContext.GetRequestId(ctx);

            await using var cmd = db.CreateCommand(@"
                SELECT out_entity_id, out_new_version, out_valid_from
                  FROM fn_feature_batch_update(@eids, @vers, @patch::jsonb, @act, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("eids", req.EntityIds!.ToArray());
            cmd.Parameters.AddWithValue("vers", req.IfMatchVersions!.ToArray());
            cmd.Parameters.AddWithValue("patch", req.AttributesPatch.GetRawText());
            cmd.Parameters.AddWithValue("act", actor);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            try
            {
                await using var r = await cmd.ExecuteReaderAsync();
                var results = new List<FeatureBatchUpdateResultDto>(req.EntityIds!.Count);
                while (await r.ReadAsync())
                {
                    results.Add(new FeatureBatchUpdateResultDto(
                        EntityId: r.GetGuid(0),
                        NewVersion: r.GetInt32(1),
                        ValidFrom: DateOnly.FromDateTime(r.GetDateTime(2))));
                }
                return Results.Ok(new FeatureBatchUpdateResponseDto(results, results.Count));
            }
            catch (PostgresException pe) when (pe.SqlState == "02000")
            {
                // entity not found
                throw new NotFoundException(pe.MessageText);
            }
            catch (PostgresException pe) when (pe.SqlState == "P0001"
                                                && pe.MessageText.Contains("optimistic_lock_failed"))
            {
                return Results.Conflict(new
                {
                    type = "https://docs.agri-gis/errors/batch-version-mismatch",
                    title = "Optimistic lock failed in batch update",
                    status = 409,
                    detail = pe.MessageText
                });
            }
        }).RequireAuthorization("WriteFeature");

        // 0212: DELETE /api/features/{entityId} (履歴退避 + current から削除)
        // F204 (Phase F WF2): entity 所属 layer の can_edit を検査。
        group.MapDelete("/{entityId:guid}",
            async (Guid entityId, HttpContext ctx, ICurrentUser user,
                   ILayerPermissionService perm, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var actor = user.DisplayName;
            var rid = RequestContext.GetRequestId(ctx);

            // WB2 B205 + F204: 論理削除レイヤの feature は触れない (404)、layer_id を取得し can_edit 検査
            int entityLayerId;
            await using (var precheck = db.CreateCommand(
                @"SELECT l.layer_id
                    FROM feature_current fc
                    JOIN layers l ON l.layer_id = fc.layer_id
                   WHERE fc.entity_id = @e AND l.valid_to = '9999-12-31'::date"))
            {
                precheck.Parameters.AddWithValue("e", entityId);
                var lid = await precheck.ExecuteScalarAsync();
                if (lid is null)
                {
                    throw new NotFoundException($"entity not found: {entityId}");
                }
                entityLayerId = (int)lid;
            }

            if (!await perm.CanEditAsync(user.OrgId, entityLayerId, user.Roles, ct))
            {
                return Results.Problem(
                    type: "https://docs.agri-gis/errors/layer-permission-denied",
                    title: "Edit denied for this layer",
                    detail: $"Your organization does not have can_edit for layer {entityLayerId}.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await using var cmd = db.CreateCommand(
                "SELECT fn_feature_delete(@e, @a, @r, @uid, @oid)");
            cmd.Parameters.AddWithValue("e", entityId);
            cmd.Parameters.AddWithValue("a", actor);
            cmd.Parameters.AddWithValue("r", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            await cmd.ExecuteScalarAsync();
            return Results.NoContent();
        }).RequireAuthorization("WriteFeature");

        return group;
    }

    // asOf=YYYY-MM-DD のみ受け付け、ISO datetime は 422
    private static DateOnly? ParseAsOf(string? asOf)
    {
        if (asOf is null)
        {
            return null;
        }
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
        {
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("asOf", "format", "asOf must be YYYY-MM-DD")
            });
        }
        return d;
    }

    // Reader → FeatureDto
    // 列順: feature_id(0), layer_id(1), entity_id(2), version(3), valid_from(4), valid_to(5),
    //       attributes_schema_version(6), created_by(7), updated_by(8), created_at(9), updated_at(10),
    //       attributes(11), geom_json(12)
    private static FeatureDto MapFeatureDto(NpgsqlDataReader r)
    {
        var attrsJson = r.GetString(11);
        var attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attrsJson, JsonOpts.Default)
                         ?? new Dictionary<string, JsonElement>();

        var geomJson = r.GetString(12);
        using var geomDoc = JsonDocument.Parse(geomJson);
        var geometry = geomDoc.RootElement.Clone();

        var properties = new FeaturePropertiesDto(
            FeatureId: r.GetInt64(0),
            LayerId: r.GetInt32(1),
            EntityId: r.GetGuid(2).ToString(),
            Version: r.GetInt32(3),
            ValidFrom: DateOnly.FromDateTime(r.GetDateTime(4)),
            ValidTo: DateOnly.FromDateTime(r.GetDateTime(5)),
            AttributesSchemaVersion: r.GetInt32(6),
            CreatedBy: r.GetString(7),
            UpdatedBy: r.GetString(8),
            CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(9), DateTimeKind.Utc)),
            UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc)),
            Attributes: attributes
        );

        return new FeatureDto("Feature", geometry, properties);
    }

    // Reader → FeatureHistoryDto
    // 列順: history_id(0), feature_id(1), layer_id(2), entity_id(3), version(4),
    //       valid_from(5), valid_to(6), attributes_schema_version(7),
    //       created_by(8), updated_by(9), created_at(10), updated_at(11),
    //       archived_at(12), archived_by(13), archived_reason(14),
    //       geom_json(15), attributes(16)
    private static FeatureHistoryDto MapFeatureHistoryDto(NpgsqlDataReader r)
    {
        var geomJson = r.GetString(15);
        using var geomDoc = JsonDocument.Parse(geomJson);
        var geometry = geomDoc.RootElement.Clone();

        var attrsJson = r.GetString(16);
        var attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attrsJson, JsonOpts.Default)
                         ?? new Dictionary<string, JsonElement>();

        return new FeatureHistoryDto(
            HistoryId: r.GetInt64(0),
            FeatureId: r.GetInt64(1),
            LayerId: r.GetInt32(2),
            EntityId: r.GetGuid(3).ToString(),
            Geometry: geometry,
            Attributes: attributes,
            AttributesSchemaVersion: r.GetInt32(7),
            ValidFrom: DateOnly.FromDateTime(r.GetDateTime(5)),
            ValidTo: DateOnly.FromDateTime(r.GetDateTime(6)),
            Version: r.GetInt32(4),
            CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc)),
            UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(11), DateTimeKind.Utc)),
            CreatedBy: r.GetString(8),
            UpdatedBy: r.GetString(9),
            ArchivedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(12), DateTimeKind.Utc)),
            ArchivedBy: r.GetString(13),
            ArchivedReason: r.GetString(14)
        );
    }
}
