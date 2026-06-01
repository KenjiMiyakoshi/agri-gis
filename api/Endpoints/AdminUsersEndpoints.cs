using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class AdminUsersEndpoints
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal) { "admin", "general", "guest" };

    public static RouteGroupBuilder MapAdminUsersEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/admin/users
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT u.user_id, u.login_id, u.display_name, u.org_id,
                       COALESCE(array_agg(ur.role ORDER BY ur.role)
                                FILTER (WHERE ur.role IS NOT NULL), '{}'),
                       u.created_at, u.updated_at
                  FROM users u
                  LEFT JOIN user_roles ur ON ur.user_id = u.user_id
                 WHERE u.deleted_at IS NULL
                 GROUP BY u.user_id, u.login_id, u.display_name, u.org_id, u.created_at, u.updated_at
                 ORDER BY u.created_at";
            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<UserDto>();
            while (await r.ReadAsync()) list.Add(ReadUserDto(r));
            return Results.Ok(list);
        });

        // POST /api/admin/users
        group.MapPost("/", async (CreateUserRequestDto req, NpgsqlDataSource db, PasswordHasher hasher) =>
        {
            ValidateCreate(req);

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var hash = hasher.Hash(req.InitialPassword);

            Guid userId;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO users (login_id, display_name, password_hash, org_id)
                       VALUES (@lid, @dn, @h, @oid)
                  RETURNING user_id", conn, tx))
            {
                ins.Parameters.AddWithValue("lid", req.LoginId);
                ins.Parameters.AddWithValue("dn", req.DisplayName);
                ins.Parameters.AddWithValue("h", hash);
                ins.Parameters.AddWithValue("oid", req.OrgId);
                userId = (Guid)(await ins.ExecuteScalarAsync())!;
            }

            foreach (var role in req.Roles.Distinct())
            {
                await using var rcmd = new NpgsqlCommand(
                    "INSERT INTO user_roles (user_id, role) VALUES (@u, @r) ON CONFLICT DO NOTHING",
                    conn, tx);
                rcmd.Parameters.AddWithValue("u", userId);
                rcmd.Parameters.AddWithValue("r", role);
                await rcmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            var dto = await LoadUserAsync(db, userId);
            return Results.Created($"/api/admin/users/{userId}", dto);
        });

        // PATCH /api/admin/users/{userId}
        group.MapPatch("/{userId:guid}", async (Guid userId, UpdateUserRequestDto req, NpgsqlDataSource db) =>
        {
            if (req.DisplayName is null && req.OrgId is null && req.Roles is null)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("body", "required", "at least one of displayName/orgId/roles must be provided")
                });
            }
            if (req.DisplayName is not null && string.IsNullOrWhiteSpace(req.DisplayName))
            {
                throw new ValidationException(new[] { new AttributeErrorDto("displayName", "required", "displayName must not be empty") });
            }
            if (req.Roles is not null) ValidateRoles(req.Roles);

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await using (var upd = new NpgsqlCommand(
                @"UPDATE users
                     SET display_name = COALESCE(@dn, display_name),
                         org_id       = COALESCE(@oid, org_id),
                         updated_at   = now()
                   WHERE user_id = @uid AND deleted_at IS NULL", conn, tx))
            {
                upd.Parameters.AddWithValue("dn", (object?)req.DisplayName ?? DBNull.Value);
                upd.Parameters.AddWithValue("oid", (object?)req.OrgId ?? DBNull.Value);
                upd.Parameters.AddWithValue("uid", userId);
                var n = await upd.ExecuteNonQueryAsync();
                if (n == 0) throw new NotFoundException($"user not found: {userId}");
            }

            if (req.Roles is not null)
            {
                await using (var del = new NpgsqlCommand("DELETE FROM user_roles WHERE user_id = @u", conn, tx))
                {
                    del.Parameters.AddWithValue("u", userId);
                    await del.ExecuteNonQueryAsync();
                }
                foreach (var role in req.Roles.Distinct())
                {
                    await using var rcmd = new NpgsqlCommand(
                        "INSERT INTO user_roles (user_id, role) VALUES (@u, @r)", conn, tx);
                    rcmd.Parameters.AddWithValue("u", userId);
                    rcmd.Parameters.AddWithValue("r", role);
                    await rcmd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();

            var dto = await LoadUserAsync(db, userId);
            return Results.Ok(dto);
        });

        // DELETE /api/admin/users/{userId} (論理削除)
        group.MapDelete("/{userId:guid}", async (Guid userId, NpgsqlDataSource db) =>
        {
            const string sql = @"
                UPDATE users
                   SET deleted_at = now(), updated_at = now()
                 WHERE user_id = @u AND deleted_at IS NULL";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("u", userId);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) throw new NotFoundException($"user not found: {userId}");
            return Results.NoContent();
        });

        // PUT /api/admin/users/{userId}/password (admin reset)
        group.MapPut("/{userId:guid}/password",
            async (Guid userId, AdminPasswordResetRequestDto req, NpgsqlDataSource db, PasswordHasher hasher) =>
        {
            if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 8)
            {
                throw new ValidationException(new[] { new AttributeErrorDto("newPassword", "minLength", "newPassword must be at least 8 characters") });
            }

            var hash = hasher.Hash(req.NewPassword);
            const string sql = @"
                UPDATE users
                   SET password_hash = @h, updated_at = now()
                 WHERE user_id = @u AND deleted_at IS NULL";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("h", hash);
            cmd.Parameters.AddWithValue("u", userId);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) throw new NotFoundException($"user not found: {userId}");
            return Results.NoContent();
        });

        return group;
    }

    private static void ValidateCreate(CreateUserRequestDto req)
    {
        var errs = new List<AttributeErrorDto>();
        if (string.IsNullOrWhiteSpace(req.LoginId)) errs.Add(new AttributeErrorDto("loginId", "required", "loginId is required"));
        if (string.IsNullOrWhiteSpace(req.DisplayName)) errs.Add(new AttributeErrorDto("displayName", "required", "displayName is required"));
        if (req.OrgId <= 0) errs.Add(new AttributeErrorDto("orgId", "required", "orgId is required"));
        if (string.IsNullOrEmpty(req.InitialPassword) || req.InitialPassword.Length < 8)
            errs.Add(new AttributeErrorDto("initialPassword", "minLength", "initialPassword must be at least 8 characters"));
        if (errs.Count > 0) throw new ValidationException(errs);
        ValidateRoles(req.Roles);
    }

    private static void ValidateRoles(IReadOnlyList<string> roles)
    {
        foreach (var r in roles)
        {
            if (!AllowedRoles.Contains(r))
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("roles", "invalid",
                        $"role '{r}' is not allowed; must be one of: {string.Join(',', AllowedRoles)}")
                });
            }
        }
    }

    private static async Task<UserDto> LoadUserAsync(NpgsqlDataSource db, Guid userId)
    {
        const string sql = @"
            SELECT u.user_id, u.login_id, u.display_name, u.org_id,
                   COALESCE(array_agg(ur.role ORDER BY ur.role)
                            FILTER (WHERE ur.role IS NOT NULL), '{}'),
                   u.created_at, u.updated_at
              FROM users u
              LEFT JOIN user_roles ur ON ur.user_id = u.user_id
             WHERE u.user_id = @u AND u.deleted_at IS NULL
             GROUP BY u.user_id, u.login_id, u.display_name, u.org_id, u.created_at, u.updated_at";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("u", userId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) throw new NotFoundException($"user not found: {userId}");
        return ReadUserDto(r);
    }

    private static UserDto ReadUserDto(NpgsqlDataReader r) =>
        new(
            UserId: r.GetGuid(0),
            LoginId: r.GetString(1),
            DisplayName: r.GetString(2),
            OrgId: r.GetInt32(3),
            Roles: (string[])r.GetValue(4),
            CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)),
            UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc)));
}
