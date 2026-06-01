using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class AdminOrgsEndpoints
{
    public static RouteGroupBuilder MapAdminOrgsEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/organizations
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT id, name, code, created_at, updated_at
                  FROM organizations
                 WHERE deleted_at IS NULL
                 ORDER BY id";
            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<OrgDto>();
            while (await r.ReadAsync())
            {
                list.Add(new OrgDto(
                    Id: r.GetInt32(0),
                    Name: r.GetString(1),
                    Code: r.GetString(2),
                    CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)),
                    UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc))));
            }
            return Results.Ok(list);
        });

        // POST /api/admin/organizations
        group.MapPost("/", async (CreateOrgRequestDto req, NpgsqlDataSource db) =>
        {
            ValidateOrgInput(req.Name, req.Code);

            const string sql = @"
                INSERT INTO organizations (name, code)
                VALUES (@n, @c)
                RETURNING id, name, code, created_at, updated_at";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("n", req.Name);
            cmd.Parameters.AddWithValue("c", req.Code);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                throw new InvalidOperationException("INSERT RETURNING returned no rows");
            }
            var dto = new OrgDto(
                Id: r.GetInt32(0),
                Name: r.GetString(1),
                Code: r.GetString(2),
                CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)),
                UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc)));
            return Results.Created($"/api/admin/organizations/{dto.Id}", dto);
        });

        // PATCH /api/admin/organizations/{id}
        group.MapPatch("/{id:int}", async (int id, UpdateOrgRequestDto req, NpgsqlDataSource db) =>
        {
            if (req.Name is not null) ValidateNonEmpty("name", req.Name);
            if (req.Code is not null) ValidateNonEmpty("code", req.Code);

            const string sql = @"
                UPDATE organizations
                   SET name       = COALESCE(@n, name),
                       code       = COALESCE(@c, code),
                       updated_at = now()
                 WHERE id = @id AND deleted_at IS NULL
             RETURNING id, name, code, created_at, updated_at";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("n", (object?)req.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("c", (object?)req.Code ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                throw new NotFoundException($"organization not found: {id}");
            }
            return Results.Ok(new OrgDto(
                Id: r.GetInt32(0),
                Name: r.GetString(1),
                Code: r.GetString(2),
                CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)),
                UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc))));
        });

        // DELETE /api/admin/organizations/{id} (論理削除)
        group.MapDelete("/{id:int}", async (int id, NpgsqlDataSource db) =>
        {
            const string sql = @"
                UPDATE organizations
                   SET deleted_at = now(), updated_at = now()
                 WHERE id = @id AND deleted_at IS NULL";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", id);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) throw new NotFoundException($"organization not found: {id}");
            return Results.NoContent();
        });

        return group;
    }

    private static void ValidateOrgInput(string name, string code)
    {
        var errs = new List<AttributeErrorDto>();
        if (string.IsNullOrWhiteSpace(name)) errs.Add(new AttributeErrorDto("name", "required", "name is required"));
        if (string.IsNullOrWhiteSpace(code)) errs.Add(new AttributeErrorDto("code", "required", "code is required"));
        if (errs.Count > 0) throw new ValidationException(errs);
    }

    private static void ValidateNonEmpty(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(new[] { new AttributeErrorDto(key, "required", $"{key} must not be empty") });
        }
    }
}
