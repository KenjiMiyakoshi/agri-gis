using AgriGis.Api.Auth;
using Npgsql;

namespace AgriGis.Api.Tests.Fixtures;

// テスト用ユーザ・組織・ロールのシード。
// 既定組織 'default' を upsert し、alice/admin / bob/general / carol/guest の 3 名を入れる。
// パスワードは全員 "TestPassword123!" (BCrypt.Net-Next で work factor 11)。
public static class SeedUsers
{
    public const string Password = "TestPassword123!";
    public const string OrgCode = "default";
    public const string AliceLogin = "alice";
    public const string BobLogin = "bob";
    public const string CarolLogin = "carol";

    public static Guid AliceId { get; private set; }
    public static Guid BobId { get; private set; }
    public static Guid CarolId { get; private set; }
    public static int OrgId { get; private set; }

    public static async Task SeedAsync(string connectionString)
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash(Password);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // 組織の upsert (deleted_at IS NULL の制約を回避するため UPDATE で復活も)
        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO organizations (name, code)
                 VALUES ('既定組織', @c)
            ON CONFLICT (code) WHERE deleted_at IS NULL
            DO UPDATE SET name = EXCLUDED.name, updated_at = now()
            RETURNING id", conn))
        {
            cmd.Parameters.AddWithValue("c", OrgCode);
            OrgId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        AliceId = await UpsertUserAsync(conn, AliceLogin, "Alice Admin", hash, OrgId, new[] { "admin" });
        BobId   = await UpsertUserAsync(conn, BobLogin,   "Bob General", hash, OrgId, new[] { "general" });
        CarolId = await UpsertUserAsync(conn, CarolLogin, "Carol Guest", hash, OrgId, new[] { "guest" });
    }

    private static async Task<Guid> UpsertUserAsync(NpgsqlConnection conn,
        string loginId, string displayName, string hash, int orgId, string[] roles)
    {
        Guid userId;
        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO users (login_id, display_name, password_hash, org_id)
                 VALUES (@l, @d, @h, @o)
            ON CONFLICT (login_id) WHERE deleted_at IS NULL
            DO UPDATE SET display_name = EXCLUDED.display_name,
                          password_hash = EXCLUDED.password_hash,
                          org_id = EXCLUDED.org_id,
                          updated_at = now()
            RETURNING user_id", conn))
        {
            cmd.Parameters.AddWithValue("l", loginId);
            cmd.Parameters.AddWithValue("d", displayName);
            cmd.Parameters.AddWithValue("h", hash);
            cmd.Parameters.AddWithValue("o", orgId);
            userId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        await using (var del = new NpgsqlCommand("DELETE FROM user_roles WHERE user_id = @u", conn))
        {
            del.Parameters.AddWithValue("u", userId);
            await del.ExecuteNonQueryAsync();
        }
        foreach (var role in roles)
        {
            await using var rcmd = new NpgsqlCommand(
                "INSERT INTO user_roles (user_id, role) VALUES (@u, @r)", conn);
            rcmd.Parameters.AddWithValue("u", userId);
            rcmd.Parameters.AddWithValue("r", role);
            await rcmd.ExecuteNonQueryAsync();
        }
        return userId;
    }
}
