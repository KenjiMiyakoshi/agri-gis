using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/auth/login (anonymous): login_id + password → JWT
        // D103 (WD1): user_sessions レコードを INSERT してから JWT に sid_session を詰める
        group.MapPost("/login", async (LoginRequestDto req, NpgsqlDataSource db,
                                       JwtService jwt, PasswordHasher hasher,
                                       IUserSessionStore sessionStore,
                                       CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrEmpty(req.Password))
            {
                return Results.Unauthorized();
            }

            const string sql = @"
                SELECT u.user_id, u.login_id, u.display_name, u.password_hash, u.org_id,
                       COALESCE(array_agg(ur.role ORDER BY ur.role)
                                FILTER (WHERE ur.role IS NOT NULL), '{}') AS roles
                  FROM users u
                  LEFT JOIN user_roles ur ON ur.user_id = u.user_id
                 WHERE u.login_id = @lid AND u.deleted_at IS NULL
                 GROUP BY u.user_id, u.login_id, u.display_name, u.password_hash, u.org_id";

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("lid", req.LoginId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return Results.Unauthorized();
            }

            var userId = r.GetGuid(0);
            var loginId = r.GetString(1);
            var displayName = r.GetString(2);
            var passwordHash = r.GetString(3);
            var orgId = r.GetInt32(4);
            var roles = (string[])r.GetValue(5);

            if (!hasher.Verify(req.Password, passwordHash))
            {
                return Results.Unauthorized();
            }

            // D103 (WD1): jti を先に確定 → user_sessions INSERT で session_id 取得 → JWT 発行
            var jti = Guid.NewGuid();
            var sessionId = await sessionStore.CreateSessionAsync(userId, jti.ToString(), ct);
            var (token, expiresAt) = jwt.IssueAccessToken(
                userId, loginId, displayName, orgId, roles, jti, sessionId);

            return Results.Ok(new LoginResponseDto(
                AccessToken: token,
                ExpiresAt: expiresAt,
                User: new UserInfoDto(userId, loginId, displayName, orgId, roles)));
        }).AllowAnonymous();

        // GET /api/auth/me (要認証): claims からユーザ情報
        group.MapGet("/me", (ICurrentUser user) =>
        {
            return Results.Ok(new UserInfoDto(
                UserId: user.UserId,
                LoginId: user.LoginId,
                DisplayName: user.DisplayName,
                OrgId: user.OrgId,
                Roles: user.Roles));
        }).RequireAuthorization();

        // POST /api/auth/change-password (要認証): 自分のパスワード変更
        group.MapPost("/change-password", async (ChangePasswordRequestDto req,
                                                  ICurrentUser user,
                                                  NpgsqlDataSource db,
                                                  PasswordHasher hasher) =>
        {
            if (string.IsNullOrEmpty(req.CurrentPassword) || string.IsNullOrEmpty(req.NewPassword))
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("password", "required", "currentPassword/newPassword are required")
                });
            }
            if (req.NewPassword.Length < 8)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("newPassword", "minLength", "newPassword must be at least 8 characters")
                });
            }

            await using (var qcmd = db.CreateCommand(
                "SELECT password_hash FROM users WHERE user_id = @uid AND deleted_at IS NULL"))
            {
                qcmd.Parameters.AddWithValue("uid", user.UserId);
                await using var qr = await qcmd.ExecuteReaderAsync();
                if (!await qr.ReadAsync())
                {
                    return Results.Unauthorized();
                }
                var currentHash = qr.GetString(0);
                if (!hasher.Verify(req.CurrentPassword, currentHash))
                {
                    return Results.Unauthorized();
                }
            }

            var newHash = hasher.Hash(req.NewPassword);
            await using (var ucmd = db.CreateCommand(
                "UPDATE users SET password_hash = @h, updated_at = now() WHERE user_id = @uid"))
            {
                ucmd.Parameters.AddWithValue("h", newHash);
                ucmd.Parameters.AddWithValue("uid", user.UserId);
                await ucmd.ExecuteNonQueryAsync();
            }

            return Results.NoContent();
        }).RequireAuthorization();

        // D204 (WD2): POST /api/auth/logout (要認証)
        // user_sessions.deleted_at = now() で session 失効
        // → 関連する selection_sets は FK CASCADE (0D04) で自動削除
        // 二重 logout は冪等 (InvalidateSessionAsync が deleted_at IS NULL 条件付き UPDATE)
        group.MapPost("/logout", async (ICurrentUser user,
                                        IUserSessionStore sessionStore,
                                        NpgsqlDataSource db,
                                        CancellationToken ct) =>
        {
            if (user.SessionId == Guid.Empty)
            {
                // sid_session claim 欠落 token は OnTokenValidated で弾かれているはずだが、
                // 防御的に 204 を返す
                return Results.NoContent();
            }
            await sessionStore.InvalidateSessionAsync(user.SessionId, ct);

            // 関連 selection_sets を明示的に削除 (FK CASCADE と二重だが、
            // user_sessions レコード自体は物理削除しないため CASCADE が発火しない)
            await using var cmd = db.CreateCommand(
                "DELETE FROM selection_sets WHERE session_id = @s");
            cmd.Parameters.AddWithValue("s", user.SessionId);
            await cmd.ExecuteNonQueryAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization();

        return group;
    }
}
