using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using AgriGis.Api.Options;
using AgriGis.Api.Shared;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// WB2 B202: /api/admin/layers CRUD。親 group RequireRole("admin") を継承。
// WB3 B203/B204 でこのファイルに bulk/import-jobs endpoint が追加される。
public static class AdminLayersEndpoints
{
    public static RouteGroupBuilder MapAdminLayersEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/layers?includeDeleted=false&asOf=YYYY-MM-DD
        // E202 (WE2): asOf 対応 (includeDeleted と排他、asOf 指定時は includeDeleted 無視)
        group.MapGet("/", async (bool? includeDeleted, string? asOf, NpgsqlDataSource db) =>
        {
            var asOfDate = AsOfParser.TryParse(asOf);
            var include = includeDeleted ?? false;
            string sql;
            // D'101 (WD'1): styleVersion を LEFT JOIN で取得 (現在 active な layer_style_version)
            // E'104 (WE'1): SELECT 句から l.deleted_at 削除、WHERE から AND l.deleted_at IS NULL 削除
            if (asOfDate is not null)
            {
                sql = @"SELECT l.layer_id, l.layer_name, l.layer_type, l.geometry_type,
                               l.source_format, l.source_srid, l.description,
                               l.schema_version, l.schema_json,
                               l.created_by, l.created_org_id,
                               l.created_at, l.updated_at,
                               COALESCE(lsv.style_version, 1) AS style_version
                          FROM layers l
                          LEFT JOIN layer_style_version lsv
                            ON lsv.layer_id = l.layer_id
                           AND lsv.valid_from <= @asof AND @asof < lsv.valid_to
                         WHERE l.valid_from <= @asof AND @asof < l.valid_to
                        UNION ALL
                        SELECT lh.layer_id, lh.layer_name, lh.layer_type, lh.geometry_type,
                               lh.source_format, lh.source_srid, lh.description,
                               lh.schema_version, lh.schema_json,
                               lh.created_by, lh.created_org_id,
                               lh.created_at, lh.updated_at,
                               COALESCE(lsv.style_version, 1) AS style_version
                          FROM layer_history lh
                          LEFT JOIN layer_style_version lsv
                            ON lsv.layer_id = lh.layer_id
                           AND lsv.valid_from <= @asof AND @asof < lsv.valid_to
                         WHERE lh.valid_from <= @asof AND @asof < lh.valid_to
                         ORDER BY layer_id";
            }
            else if (include)
            {
                sql = @"SELECT l.layer_id, l.layer_name, l.layer_type, l.geometry_type,
                               l.source_format, l.source_srid, l.description,
                               l.schema_version, l.schema_json,
                               l.created_by, l.created_org_id,
                               l.created_at, l.updated_at,
                               COALESCE(lsv.style_version, 1) AS style_version
                          FROM layers l
                          LEFT JOIN layer_style_version lsv
                            ON lsv.layer_id = l.layer_id
                           AND lsv.valid_to = '9999-12-31'::date
                         ORDER BY l.layer_id";
            }
            else
            {
                sql = @"SELECT l.layer_id, l.layer_name, l.layer_type, l.geometry_type,
                               l.source_format, l.source_srid, l.description,
                               l.schema_version, l.schema_json,
                               l.created_by, l.created_org_id,
                               l.created_at, l.updated_at,
                               COALESCE(lsv.style_version, 1) AS style_version
                          FROM layers l
                          LEFT JOIN layer_style_version lsv
                            ON lsv.layer_id = l.layer_id
                           AND lsv.valid_to = '9999-12-31'::date
                         WHERE l.valid_to = '9999-12-31'::date
                         ORDER BY l.layer_id";
            }
            await using var cmd = db.CreateCommand(sql);
            if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
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

        // PATCH /api/admin/layers/{id} — E202 (WE2): fn_layer_update 経由化
        // If-Match: {version} ヘッダ任意。未指定時は現在 version を内部 SELECT で取得 (Phase E)。
        group.MapPatch("/{layerId:int}",
            async (int layerId, UpdateLayerRequestDto req, ICurrentUser user, HttpContext ctx, NpgsqlDataSource db) =>
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

            // version 取得: If-Match ヘッダ優先、なければ DB から現在値
            int expectedVersion;
            var ifMatch = ctx.Request.Headers["If-Match"].ToString();
            if (!string.IsNullOrEmpty(ifMatch) && int.TryParse(ifMatch.Trim('"'), out var parsedVersion))
            {
                expectedVersion = parsedVersion;
            }
            else
            {
                await using var vcmd = db.CreateCommand(
                    "SELECT version FROM layers WHERE layer_id = @id AND valid_to = '9999-12-31'::date");
                vcmd.Parameters.AddWithValue("id", layerId);
                var v = await vcmd.ExecuteScalarAsync();
                if (v is null) throw new NotFoundException($"layer not found: {layerId}");
                expectedVersion = (int)v;
            }

            var rid = RequestContext.GetRequestId(ctx);
            await using var cmd = db.CreateCommand(@"
                SELECT * FROM fn_layer_update(
                    p_layer_id => @id,
                    p_layer_name => @n,
                    p_layer_type => @lt,
                    p_geometry_type => @gt,
                    p_description => @desc,
                    p_source_format => NULL,
                    p_source_srid => NULL,
                    p_expected_version => @ev,
                    p_actor => @act,
                    p_request_id => @rid,
                    p_user_id => @uid,
                    p_org_id => @oid)");
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("n",    (object?)req.LayerName    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("lt",   (object?)req.LayerType    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("gt",   (object?)req.GeometryType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("desc", (object?)req.Description  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ev", expectedVersion);
            cmd.Parameters.AddWithValue("act", user.DisplayName);
            cmd.Parameters.AddWithValue("rid", rid);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);
            try
            {
                await cmd.ExecuteScalarAsync();
            }
            catch (PostgresException pe) when (pe.SqlState == "02000")
            {
                throw new NotFoundException($"layer not found: {layerId}");
            }
            catch (PostgresException pe) when (pe.SqlState == "P0001")
            {
                return Results.Conflict(new { title = "optimistic lock violation", layerId, expectedVersion, detail = pe.Message });
            }

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
                "SELECT 1 FROM layers WHERE layer_id = @id AND valid_to = '9999-12-31'::date", conn))
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
                "SELECT schema_version, schema_json FROM layers WHERE layer_id = @id AND valid_to = '9999-12-31'::date", conn))
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

        // D'105 (WD'1): GET /api/admin/layers/{layerId}/attributes/{field}/stats?bins=N&method=quantile|equal
        // 数値属性のカラーランプ自動生成のための breaks 計算。
        // - quantile: PostgreSQL percentile_cont で等分位 (default)
        // - equal:    [min, max] を等間隔
        // field 名は alphanumeric + underscore のみ (SQL injection 防止)
        group.MapGet("/{layerId:int}/attributes/{field}/stats", async (
            int layerId, string field, int? bins, string? method, NpgsqlDataSource db) =>
        {
            var b = bins ?? 5;
            if (b < 2 || b > 20)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("bins", "range", "bins must be in [2, 20]")
                });
            }
            var m = (method ?? "quantile").ToLowerInvariant();
            if (m != "quantile" && m != "equal")
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("method", "invalid", "method must be 'quantile' or 'equal'")
                });
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(field, @"^[a-zA-Z0-9_]{1,64}$"))
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("field", "format",
                        "field must match ^[a-zA-Z0-9_]{1,64}$")
                });
            }

            // min/max/count
            var statsSql = $@"
                SELECT MIN(v), MAX(v), COUNT(*)
                  FROM (
                    SELECT (attributes->>'{field}')::numeric AS v
                      FROM feature_current
                     WHERE layer_id = @id
                       AND attributes ? '{field}'
                       AND attributes->>'{field}' IS NOT NULL
                     LIMIT 50000
                  ) samples";
            double mn = 0, mx = 0;
            long cnt = 0;
            await using (var sc = db.CreateCommand(statsSql))
            {
                sc.Parameters.AddWithValue("id", layerId);
                await using var sr = await sc.ExecuteReaderAsync();
                if (!await sr.ReadAsync() || sr.IsDBNull(0))
                {
                    return Results.Ok(new
                    {
                        field,
                        method = m,
                        bins = b,
                        breaks = Array.Empty<double>(),
                        min = 0.0,
                        max = 0.0,
                        count = 0L
                    });
                }
                mn = (double)sr.GetDecimal(0);
                mx = (double)sr.GetDecimal(1);
                cnt = sr.GetInt64(2);
            }

            double[] breaks;
            if (m == "equal")
            {
                breaks = new double[b];
                for (int i = 0; i < b; i++)
                {
                    breaks[i] = mn + (mx - mn) * (i + 1) / b;
                }
            }
            else
            {
                var pcts = new double[b];
                for (int i = 0; i < b; i++)
                {
                    pcts[i] = (i + 1.0) / b;
                }
                var qSql = $@"
                    SELECT percentile_cont(@pcts) WITHIN GROUP (ORDER BY v)
                      FROM (
                        SELECT (attributes->>'{field}')::numeric AS v
                          FROM feature_current
                         WHERE layer_id = @id
                           AND attributes ? '{field}'
                           AND attributes->>'{field}' IS NOT NULL
                         LIMIT 50000
                      ) samples";
                await using var qc = db.CreateCommand(qSql);
                qc.Parameters.AddWithValue("id", layerId);
                qc.Parameters.AddWithValue("pcts", pcts);
                var raw = await qc.ExecuteScalarAsync();
                // percentile_cont は numeric[] を返す。double[] にキャスト
                if (raw is decimal[] decs)
                {
                    breaks = decs.Select(d => (double)d).ToArray();
                }
                else if (raw is double[] dbls)
                {
                    breaks = dbls;
                }
                else
                {
                    breaks = Array.Empty<double>();
                }
            }

            return Results.Ok(new
            {
                field,
                method = m,
                bins = b,
                breaks,
                min = mn,
                max = mx,
                count = cnt
            });
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
        // D'101 (WD'1): styleVersion を LEFT JOIN で取得
        // E'104 (WE'1): l.deleted_at 削除
        const string sql = @"
            SELECT l.layer_id, l.layer_name, l.layer_type, l.geometry_type,
                   l.source_format, l.source_srid, l.description,
                   l.schema_version, l.schema_json,
                   l.created_by, l.created_org_id,
                   l.created_at, l.updated_at,
                   COALESCE(lsv.style_version, 1) AS style_version
              FROM layers l
              LEFT JOIN layer_style_version lsv
                ON lsv.layer_id = l.layer_id
               AND lsv.valid_to = '9999-12-31'::date
             WHERE l.layer_id = @id";
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

        // E'104 (WE'1): DeletedAt 削除、StyleVersion を index 13 に
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
            StyleVersion:  r.GetInt32(13));
    }
}
