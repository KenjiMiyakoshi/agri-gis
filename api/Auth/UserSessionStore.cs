using Npgsql;

namespace AgriGis.Api.Auth;

// D103 (WD1): IUserSessionStore の Npgsql 実装。
// 単純 SQL 3 本。インデックス ux_user_sessions_jti_alive / ix_user_sessions_active を前提に O(log N)。
public sealed class UserSessionStore : IUserSessionStore
{
    private readonly NpgsqlDataSource _db;

    public UserSessionStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    public async Task<Guid> CreateSessionAsync(Guid userId, string jwtJti, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO user_sessions (user_id, jwt_jti)
            VALUES (@u, @j)
            RETURNING session_id";

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("j", jwtJti);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is Guid g) return g;
        throw new InvalidOperationException("user_sessions INSERT did not return session_id");
    }

    public async Task InvalidateSessionAsync(Guid sessionId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE user_sessions
               SET deleted_at = now()
             WHERE session_id = @s AND deleted_at IS NULL";

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("s", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken ct)
    {
        const string sql = @"
            SELECT 1
              FROM user_sessions
             WHERE session_id = @s AND deleted_at IS NULL";

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("s", sessionId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }
}
