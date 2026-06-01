using Npgsql;

namespace AgriGis.Api.Auth;

// 起動時に admin ロール保有ユーザが居なければ初期 admin を作成する IHostedService。
// パスワードは環境変数 AGRI_GIS_INITIAL_ADMIN_PW から取得。未設定なら fail-fast。
// 既定組織 (code='default') を作成・参照し、admin ユーザを upsert する。
public sealed class InitialAdminBootstrap : IHostedService
{
    private const string DefaultOrgCode = "default";
    private const string DefaultOrgName = "既定組織";
    private const string AdminLoginId = "admin";
    private const string AdminDisplayName = "Administrator";

    private readonly NpgsqlDataSource _db;
    private readonly PasswordHasher _hasher;
    private readonly ILogger<InitialAdminBootstrap> _log;

    public InitialAdminBootstrap(NpgsqlDataSource db, PasswordHasher hasher, ILogger<InitialAdminBootstrap> log)
    {
        _db = db;
        _hasher = hasher;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var pw = Environment.GetEnvironmentVariable("AGRI_GIS_INITIAL_ADMIN_PW");
        if (string.IsNullOrEmpty(pw))
        {
            throw new InvalidOperationException(
                "AGRI_GIS_INITIAL_ADMIN_PW environment variable is not set. Required for initial admin bootstrap.");
        }

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 既に admin ロール保有ユーザがあればスキップ
        await using (var check = new NpgsqlCommand(
            @"SELECT 1 FROM user_roles ur
                JOIN users u ON u.user_id = ur.user_id
               WHERE ur.role = 'admin' AND u.deleted_at IS NULL
               LIMIT 1", conn, tx))
        {
            var found = await check.ExecuteScalarAsync(ct);
            if (found is not null)
            {
                _log.LogInformation("InitialAdminBootstrap: admin user already exists, skip");
                await tx.CommitAsync(ct);
                return;
            }
        }

        // 既定組織を取得 or 作成
        int orgId;
        await using (var orgCmd = new NpgsqlCommand(
            @"WITH ins AS (
                INSERT INTO organizations (name, code)
                     VALUES (@n, @c)
                ON CONFLICT DO NOTHING
                  RETURNING id
              )
              SELECT id FROM ins
              UNION ALL
              SELECT id FROM organizations WHERE code = @c AND deleted_at IS NULL
              LIMIT 1", conn, tx))
        {
            orgCmd.Parameters.AddWithValue("n", DefaultOrgName);
            orgCmd.Parameters.AddWithValue("c", DefaultOrgCode);
            orgId = Convert.ToInt32(await orgCmd.ExecuteScalarAsync(ct));
        }

        var hash = _hasher.Hash(pw);

        // 既存 admin login_id があれば password を更新、無ければ INSERT
        Guid userId;
        await using (var upsertUser = new NpgsqlCommand(
            @"INSERT INTO users (login_id, display_name, password_hash, org_id)
                  VALUES (@lid, @dn, @h, @oid)
              ON CONFLICT (login_id) WHERE deleted_at IS NULL
              DO UPDATE SET password_hash = EXCLUDED.password_hash,
                            display_name  = EXCLUDED.display_name,
                            updated_at    = now()
              RETURNING user_id", conn, tx))
        {
            upsertUser.Parameters.AddWithValue("lid", AdminLoginId);
            upsertUser.Parameters.AddWithValue("dn", AdminDisplayName);
            upsertUser.Parameters.AddWithValue("h", hash);
            upsertUser.Parameters.AddWithValue("oid", orgId);
            userId = (Guid)(await upsertUser.ExecuteScalarAsync(ct))!;
        }

        // admin ロールを付与（重複は無視）
        await using (var roleCmd = new NpgsqlCommand(
            @"INSERT INTO user_roles (user_id, role)
                  VALUES (@uid, 'admin')
              ON CONFLICT (user_id, role) DO NOTHING", conn, tx))
        {
            roleCmd.Parameters.AddWithValue("uid", userId);
            await roleCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        _log.LogInformation("InitialAdminBootstrap: created admin user (user_id={UserId}, org_id={OrgId})", userId, orgId);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
