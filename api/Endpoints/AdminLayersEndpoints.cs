using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
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

        return group;
    }

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
