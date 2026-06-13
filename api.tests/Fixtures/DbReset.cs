using Npgsql;

namespace AgriGis.Api.Tests.Fixtures;

// テストごとに走らせて行数を予測可能な状態にする。
// WB1 (B101) 以降は layers.created_by / created_org_id が FK 付きで存在するため、
// 順序を「users → layers」に変更し、alice の UUID + 既定組織 id を seed layers に紐付ける。
public static class DbReset
{
    // E'101 (WE'1): Phase E で追加された layer_style_version / layer_history も TRUNCATE 対象に
    // (テスト full run で並列耐性問題を起こす原因の 1 つ。test-isolation.md 参照)
    // F (Phase F WF2): org_layer_permission も TRUNCATE 対象に追加
    private const string TruncateSql = @"
        TRUNCATE audit_log RESTART IDENTITY CASCADE;
        TRUNCATE feature_current, feature_history, layer_schema_version, layer_style_version, layer_history
                 RESTART IDENTITY CASCADE;
        DELETE FROM org_layer_permission;
        DELETE FROM layer_import_job;
        -- LGP106: layer_group_member は layer_group / layers を FK 参照するため先に消す
        DELETE FROM layer_group_member;
        DELETE FROM layers;
        ALTER SEQUENCE layers_layer_id_seq RESTART WITH 1;
        DELETE FROM layer_group;
        ALTER SEQUENCE layer_group_group_id_seq RESTART WITH 1;
        DELETE FROM user_roles;
        DELETE FROM users;
        DELETE FROM organizations;
        ALTER SEQUENCE organizations_id_seq RESTART WITH 1;";

    public static async Task RunAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var c = new NpgsqlCommand(TruncateSql, conn))
        {
            await c.ExecuteNonQueryAsync();
        }

        // 1. ユーザ seed (alice/bob/carol)。layers.created_by の FK 解決に必要
        await SeedUsers.SeedAsync(connectionString);

        // 2. layers seed (created_by = alice, created_org_id = default org)
        const string layersSeedSql = @"
            INSERT INTO layers (
                layer_name, layer_type, geometry_type, schema_json, schema_version,
                created_by, created_org_id
            ) VALUES
              ('サンプル圃場', 'polygon', 'Polygon',
               '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true},{""key"":""crop"",""type"":""string"",""required"":false}]}'::jsonb,
               1, @alice, @org),
              ('サンプル観測点', 'point', 'Point',
               '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true}]}'::jsonb,
               1, @alice, @org);
            INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
            SELECT layer_id, schema_version, schema_json, now(), NULL, 'system' FROM layers;";

        await using (var c = new NpgsqlCommand(layersSeedSql, conn))
        {
            c.Parameters.AddWithValue("alice", SeedUsers.AliceId);
            c.Parameters.AddWithValue("org",   SeedUsers.OrgId);
            await c.ExecuteNonQueryAsync();
        }

        // 3. F (Phase F WF2): org_layer_permission seed
        //    既定組織 × 全 seed layer を can_view=true / can_edit=true で投入
        //    (一般 user 役の bob は can_edit=true 環境で動く想定。
        //     can_edit=false テストは個別に GrantPermissionAsync で上書きする)
        const string permSeedSql = @"
            INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit)
            SELECT @org, l.layer_id, true, true
              FROM layers l
             WHERE l.valid_to = '9999-12-31'::date
            ON CONFLICT DO NOTHING;";
        await using (var c = new NpgsqlCommand(permSeedSql, conn))
        {
            c.Parameters.AddWithValue("org", SeedUsers.OrgId);
            await c.ExecuteNonQueryAsync();
        }
    }

    // LGP106 (Phase LG' WLGP1): 2 つ目の組織 + その admin ユーザを作る。
    // org スコープ検証 (org A の group が org B に出ない 等) に使う。
    // 戻り値の (orgId, userId, loginId, displayName) を TokenForge.Issue に渡してトークンを発行する。
    public sealed record SecondOrg(int OrgId, Guid UserId, string LoginId, string DisplayName);

    public static async Task<SecondOrg> SeedSecondOrgAsync(
        string connectionString, string code = "org-b", string adminLogin = "betty")
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        int orgId;
        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO organizations (name, code)
                 VALUES (@n, @c)
            ON CONFLICT (code) WHERE deleted_at IS NULL
            DO UPDATE SET name = EXCLUDED.name, updated_at = now()
            RETURNING id", conn))
        {
            cmd.Parameters.AddWithValue("n", $"組織 {code}");
            cmd.Parameters.AddWithValue("c", code);
            orgId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var hasher = new AgriGis.Api.Auth.PasswordHasher();
        var hash = hasher.Hash(SeedUsers.Password);
        var displayName = $"{adminLogin} Admin";

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
            cmd.Parameters.AddWithValue("l", adminLogin);
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
        await using (var rcmd = new NpgsqlCommand(
            "INSERT INTO user_roles (user_id, role) VALUES (@u, 'admin')", conn))
        {
            rcmd.Parameters.AddWithValue("u", userId);
            await rcmd.ExecuteNonQueryAsync();
        }

        return new SecondOrg(orgId, userId, adminLogin, displayName);
    }

    // F (Phase F WF2): テスト用の権限上書きヘルパ。
    // 指定 (orgId, layerId) について can_view / can_edit を upsert する。
    public static async Task SetPermissionAsync(
        string connectionString, int orgId, int layerId, bool canView, bool canEdit)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit)
            VALUES (@oid, @lid, @cv, @ce)
            ON CONFLICT (org_id, layer_id)
            DO UPDATE SET can_view = EXCLUDED.can_view,
                          can_edit = EXCLUDED.can_edit,
                          updated_at = now()", conn);
        cmd.Parameters.AddWithValue("oid", orgId);
        cmd.Parameters.AddWithValue("lid", layerId);
        cmd.Parameters.AddWithValue("cv", canView);
        cmd.Parameters.AddWithValue("ce", canEdit);
        await cmd.ExecuteNonQueryAsync();
    }
}
