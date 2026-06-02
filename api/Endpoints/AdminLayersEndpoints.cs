using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using AgriGis.Api.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// WB2 B202: /api/admin/layers CRUD。親 group RequireRole("admin") を継承。
// WB3 B203/B204 でこのファイルに bulk/import-jobs endpoint が追加される。
public static class AdminLayersEndpoints
{
    public static RouteGroupBuilder MapAdminLayersEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/layers?includeDeleted=false
        group.MapGet("/", async (bool? includeDeleted, NpgsqlDataSource db) =>
        {
            var include = includeDeleted ?? false;
            var sql = include
                ? @"SELECT layer_id, layer_name, layer_type, geometry_type,
                           source_format, source_srid, description,
                           schema_version, schema_json,
                           created_by, created_org_id,
                           created_at, updated_at, deleted_at
                      FROM layers
                     ORDER BY layer_id"
                : @"SELECT layer_id, layer_name, layer_type, geometry_type,
                           source_format, source_srid, description,
                           schema_version, schema_json,
                           created_by, created_org_id,
                           created_at, updated_at, deleted_at
                      FROM layers
                     WHERE deleted_at IS NULL
                     ORDER BY layer_id";
            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<LayerAdminDto>();
            while (await r.ReadAsync()) list.Add(MapLayerAdminDto(r));
            return Results.Ok(list);
        });

        // POST /api/admin/layers
        group.MapPost("/", async (CreateLayerRequestDto req, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            ValidateCreate(req);

            var schemaJson = req.Schema is null
                ? "{\"fields\":[]}"
                : JsonSerializer.Serialize(req.Schema, JsonOpts.Default);

            var rid = RequestContext.GetRequestId(ctx);
            await using var cmd = db.CreateCommand(
                "SELECT fn_layer_create(@n, @lt, @gt, @sf, @ss, @desc, @sj::jsonb, @act, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("n", req.LayerName);
            cmd.Parameters.AddWithValue("lt", req.LayerType);
            cmd.Parameters.AddWithValue("gt", (object?)req.GeometryType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sf", (object?)req.SourceFormat ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ss", (object?)req.SourceSrid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("desc", (object?)req.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sj", schemaJson);
            cmd.Parameters.AddWithValue("act", user.DisplayName);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            var layerId = (int)(await cmd.ExecuteScalarAsync())!;
            var dto = await LoadLayerAsync(db, layerId);
            return Results.Created($"/api/admin/layers/{layerId}", dto);
        });

        // GET /api/admin/layers/{id}
        group.MapGet("/{layerId:int}", async (int layerId, NpgsqlDataSource db) =>
        {
            try
            {
                var dto = await LoadLayerAsync(db, layerId);
                return Results.Ok(dto);
            }
            catch (NotFoundException)
            {
                return Results.NotFound();
            }
        });

        // PATCH /api/admin/layers/{id}
        group.MapPatch("/{layerId:int}",
            async (int layerId, UpdateLayerRequestDto req, ICurrentUser user, NpgsqlDataSource db) =>
        {
            if (req.LayerName is null && req.LayerType is null
                && req.GeometryType is null && req.Description is null)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("body", "required",
                        "at least one of layerName/layerType/geometryType/description must be provided")
                });
            }
            if (req.LayerName is not null) ValidateNonEmpty("layerName", req.LayerName);
            if (req.LayerType is not null) ValidateNonEmpty("layerType", req.LayerType);

            const string sql = @"
                UPDATE layers
                   SET layer_name    = COALESCE(@n,  layer_name),
                       layer_type    = COALESCE(@lt, layer_type),
                       geometry_type = COALESCE(@gt, geometry_type),
                       description   = COALESCE(@desc, description),
                       updated_at    = now()
                 WHERE layer_id = @id AND deleted_at IS NULL";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("n",    (object?)req.LayerName    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("lt",   (object?)req.LayerType    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("gt",   (object?)req.GeometryType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("desc", (object?)req.Description  ?? DBNull.Value);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) throw new NotFoundException($"layer not found: {layerId}");

            var dto = await LoadLayerAsync(db, layerId);
            return Results.Ok(dto);
        });

        // DELETE /api/admin/layers/{id}
        group.MapDelete("/{layerId:int}",
            async (int layerId, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            // 409: import job が running 中なら削除拒否 (実装リスク 6)
            await using (var jcmd = db.CreateCommand(
                @"SELECT 1 FROM layer_import_job
                   WHERE layer_id = @id AND status = 'running' LIMIT 1"))
            {
                jcmd.Parameters.AddWithValue("id", layerId);
                if (await jcmd.ExecuteScalarAsync() is not null)
                {
                    return Results.Conflict(new
                    {
                        title = "Layer has a running import job",
                        layerId
                    });
                }
            }

            var rid = RequestContext.GetRequestId(ctx);
            await using var cmd = db.CreateCommand(
                "SELECT fn_layer_delete(@id, @act, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("act", user.DisplayName);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);
            await cmd.ExecuteScalarAsync();
            return Results.NoContent();
        });

        // WB3 B204: POST /api/admin/layers/{id}/import-jobs (start)
        group.MapPost("/{layerId:int}/import-jobs",
            async (int layerId, StartImportJobRequestDto req, ICurrentUser user, NpgsqlDataSource db) =>
        {
            // 409: 既に running ジョブあり、または layer 存在/論理削除確認
            await using var conn = await db.OpenConnectionAsync();
            await using (var lcmd = new NpgsqlCommand(
                "SELECT 1 FROM layers WHERE layer_id = @id AND deleted_at IS NULL", conn))
            {
                lcmd.Parameters.AddWithValue("id", layerId);
                if (await lcmd.ExecuteScalarAsync() is null)
                    throw new NotFoundException($"layer not found: {layerId}");
            }
            await using (var rcmd = new NpgsqlCommand(
                "SELECT 1 FROM layer_import_job WHERE layer_id = @id AND status = 'running' LIMIT 1", conn))
            {
                rcmd.Parameters.AddWithValue("id", layerId);
                if (await rcmd.ExecuteScalarAsync() is not null)
                {
                    return Results.Conflict(new { title = "Another import job is already running for this layer", layerId });
                }
            }

            var jobId = Guid.NewGuid();
            await using (var ins = new NpgsqlCommand(@"
                INSERT INTO layer_import_job
                       (job_id, layer_id, status, total_count, created_by, created_org_id)
                VALUES (@jid, @lid, 'running', @tc, @uid, @oid)
                RETURNING job_id, layer_id, status, total_count, inserted_count,
                          started_at, finished_at, error_text", conn))
            {
                ins.Parameters.AddWithValue("jid", jobId);
                ins.Parameters.AddWithValue("lid", layerId);
                ins.Parameters.AddWithValue("tc", (object?)req.TotalCount ?? DBNull.Value);
                ins.Parameters.AddWithValue("uid", user.UserId);
                ins.Parameters.AddWithValue("oid", user.OrgId);
                await using var r = await ins.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    throw new InvalidOperationException("INSERT RETURNING returned no rows");
                var dto = MapImportJobDto(r);
                return Results.Created($"/api/admin/layers/import-jobs/{jobId}", dto);
            }
        });

        // WB3 B204: GET /api/admin/layers/import-jobs/{jobId}
        group.MapGet("/import-jobs/{jobId:guid}", async (Guid jobId, NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT job_id, layer_id, status, total_count, inserted_count,
                       started_at, finished_at, error_text
                  FROM layer_import_job
                 WHERE job_id = @jid";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("jid", jobId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.NotFound();
            return Results.Ok(MapImportJobDto(r));
        });

        // WB3 B204: POST /api/admin/layers/import-jobs/{jobId}/finalize
        group.MapPost("/import-jobs/{jobId:guid}/finalize",
            async (Guid jobId, FinalizeImportJobRequestDto req, NpgsqlDataSource db) =>
        {
            if (req.Status != "succeeded" && req.Status != "failed")
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("status", "invalid", "status must be 'succeeded' or 'failed'")
                });
            }

            const string sql = @"
                UPDATE layer_import_job
                   SET status     = @s,
                       error_text = @et,
                       finished_at = now()
                 WHERE job_id = @jid AND status = 'running'
              RETURNING job_id, layer_id, status, total_count, inserted_count,
                        started_at, finished_at, error_text";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("jid", jobId);
            cmd.Parameters.AddWithValue("s", req.Status);
            cmd.Parameters.AddWithValue("et", (object?)req.ErrorText ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                // job not found OR already finalized
                await using var existCmd = db.CreateCommand(
                    "SELECT status FROM layer_import_job WHERE job_id = @jid");
                existCmd.Parameters.AddWithValue("jid", jobId);
                var existingStatus = (string?)await existCmd.ExecuteScalarAsync();
                if (existingStatus is null) return Results.NotFound();
                return Results.Conflict(new { title = $"Import job already finalized: status={existingStatus}", jobId });
            }
            return Results.Ok(MapImportJobDto(r));
        });

        // WB3 B203: POST /api/admin/layers/{id}/features/bulk (チャンク 1 回分)
        group.MapPost("/{layerId:int}/features/bulk",
            async (int layerId, BulkFeaturesRequestDto req,
                   HttpContext ctx, ICurrentUser user,
                   NpgsqlDataSource db, IOptions<BulkInsertOptions> bulkOpts) =>
        {
            var opts = bulkOpts.Value;
            if (req.Features is null || req.Features.Count == 0)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("features", "required", "features must be non-empty")
                });
            }
            if (req.Features.Count > opts.MaxCountPerChunk)
            {
                return Results.Problem(
                    title: "Chunk too large",
                    detail: $"features count {req.Features.Count} exceeds MaxCountPerChunk {opts.MaxCountPerChunk}",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            // job 検証
            await using var conn = await db.OpenConnectionAsync();
            await using (var jcmd = new NpgsqlCommand(
                "SELECT status, layer_id FROM layer_import_job WHERE job_id = @jid", conn))
            {
                jcmd.Parameters.AddWithValue("jid", req.JobId);
                await using var jr = await jcmd.ExecuteReaderAsync();
                if (!await jr.ReadAsync())
                    throw new NotFoundException($"import job not found: {req.JobId}");
                var status = jr.GetString(0);
                var jobLayerId = jr.GetInt32(1);
                if (status != "running")
                    return Results.Conflict(new { title = $"Import job not running: status={status}", jobId = req.JobId });
                if (jobLayerId != layerId)
                    return Results.Conflict(new { title = "JobId does not match layerId", jobId = req.JobId, expectedLayerId = jobLayerId });
            }

            // schema 取得 (バリデーション用)
            LayerSchemaDto schema;
            int schemaVersion;
            await using (var sc = new NpgsqlCommand(
                "SELECT schema_version, schema_json FROM layers WHERE layer_id = @id AND deleted_at IS NULL", conn))
            {
                sc.Parameters.AddWithValue("id", layerId);
                await using var sr = await sc.ExecuteReaderAsync();
                if (!await sr.ReadAsync()) throw new NotFoundException($"layer not found: {layerId}");
                schemaVersion = sr.GetInt32(0);
                schema = JsonSerializer.Deserialize<LayerSchemaDto>(sr.GetString(1), JsonOpts.Default)
                         ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());
            }

            // チャンク単位の request_id (1 chunk = 1 UUID、PHASE_B_DESIGN_P §5.2.2)
            var chunkRequestId = Guid.NewGuid().ToString();

            // Tx 中に例外が出れば await using の Dispose が rollback する
            var insertedIds = new List<long>(req.Features.Count);
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var f in req.Features)
            {
                var attrs = f.Properties ?? new Dictionary<string, JsonElement>();
                var errs = AgriGis.Api.Validation.AttributeValidator.Validate(schema, attrs);
                if (errs.Count > 0)
                {
                    throw new ValidationException(errs);
                }
                var entityId = Guid.NewGuid();
                await using var icmd = new NpgsqlCommand(
                    "SELECT fn_feature_insert(@l, @e, @g, @a::jsonb, @act, @r, @u, @o)", conn, tx);
                icmd.Parameters.AddWithValue("l", layerId);
                icmd.Parameters.AddWithValue("e", entityId);
                icmd.Parameters.AddWithValue("g", f.Geometry.GetRawText());
                icmd.Parameters.AddWithValue("a", JsonSerializer.Serialize(attrs, JsonOpts.Default));
                icmd.Parameters.AddWithValue("act", user.DisplayName);
                icmd.Parameters.AddWithValue("r", chunkRequestId);
                icmd.Parameters.AddWithValue("u", user.UserId);
                icmd.Parameters.AddWithValue("o", user.OrgId);
                insertedIds.Add((long)(await icmd.ExecuteScalarAsync())!);
            }

            // ジョブ進捗 += chunk (同 Tx 内で完了させる)
            await using (var jp = new NpgsqlCommand(@"
                UPDATE layer_import_job
                   SET inserted_count = inserted_count + @n
                 WHERE job_id = @jid AND status = 'running'", conn, tx))
            {
                jp.Parameters.AddWithValue("n", insertedIds.Count);
                jp.Parameters.AddWithValue("jid", req.JobId);
                await jp.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Results.Ok(new BulkFeaturesResponseDto(InsertedCount: insertedIds.Count, FeatureIds: insertedIds));
        });

        return group;
    }

    private static ImportJobDto MapImportJobDto(NpgsqlDataReader r) =>
        new(
            JobId:         r.GetGuid(0),
            LayerId:       r.GetInt32(1),
            Status:        r.GetString(2),
            TotalCount:    r.IsDBNull(3) ? null : r.GetInt32(3),
            InsertedCount: r.GetInt32(4),
            StartedAt:     new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)),
            FinishedAt:    r.IsDBNull(6) ? null
                : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc)),
            ErrorText:     r.IsDBNull(7) ? null : r.GetString(7));

    private static void ValidateCreate(CreateLayerRequestDto req)
    {
        var errs = new List<AttributeErrorDto>();
        if (string.IsNullOrWhiteSpace(req.LayerName))
            errs.Add(new AttributeErrorDto("layerName", "required", "layerName is required"));
        if (string.IsNullOrWhiteSpace(req.LayerType))
            errs.Add(new AttributeErrorDto("layerType", "required", "layerType is required"));
        if (errs.Count > 0) throw new ValidationException(errs);
    }

    private static void ValidateNonEmpty(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException(new[]
                { new AttributeErrorDto(key, "required", $"{key} must not be empty") });
    }

    private static async Task<LayerAdminDto> LoadLayerAsync(NpgsqlDataSource db, int layerId)
    {
        const string sql = @"
            SELECT layer_id, layer_name, layer_type, geometry_type,
                   source_format, source_srid, description,
                   schema_version, schema_json,
                   created_by, created_org_id,
                   created_at, updated_at, deleted_at
              FROM layers WHERE layer_id = @id";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", layerId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) throw new NotFoundException($"layer not found: {layerId}");
        return MapLayerAdminDto(r);
    }

    private static LayerAdminDto MapLayerAdminDto(NpgsqlDataReader r)
    {
        var schemaJson = r.GetString(8);
        var schema = JsonSerializer.Deserialize<LayerSchemaDto>(schemaJson, JsonOpts.Default)
                     ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>());

        return new LayerAdminDto(
            LayerId:       r.GetInt32(0),
            LayerName:     r.GetString(1),
            LayerType:     r.GetString(2),
            GeometryType:  r.IsDBNull(3) ? null : r.GetString(3),
            SourceFormat:  r.IsDBNull(4) ? null : r.GetString(4),
            SourceSrid:    r.IsDBNull(5) ? null : r.GetInt32(5),
            Description:   r.IsDBNull(6) ? null : r.GetString(6),
            SchemaVersion: r.GetInt32(7),
            Schema:        schema,
            CreatedBy:     r.IsDBNull(9) ? null : r.GetGuid(9),
            CreatedOrgId:  r.IsDBNull(10) ? null : r.GetInt32(10),
            CreatedAt:     new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(11), DateTimeKind.Utc)),
            UpdatedAt:     new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(12), DateTimeKind.Utc)),
            DeletedAt:     r.IsDBNull(13) ? null
                : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(13), DateTimeKind.Utc)));
    }
}
