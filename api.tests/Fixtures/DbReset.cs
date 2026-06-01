using Npgsql;

namespace AgriGis.Api.Tests.Fixtures;

// テストごとに走らせて行数を予測可能な状態にする。
// feature_current/history/audit/layer_schema_version/layers/user_roles/users/organizations
// を初期化し、2 レイヤの基本シードと 3 ユーザ (alice/bob/carol) を入れる。
public static class DbReset
{
    private const string TruncateSql = @"
        TRUNCATE audit_log RESTART IDENTITY CASCADE;
        TRUNCATE feature_current, feature_history, layer_schema_version
                 RESTART IDENTITY CASCADE;
        DELETE FROM layers;
        ALTER SEQUENCE layers_layer_id_seq RESTART WITH 1;
        DELETE FROM user_roles;
        DELETE FROM users;
        DELETE FROM organizations;
        ALTER SEQUENCE organizations_id_seq RESTART WITH 1;";

    private const string LayersSeedSql = @"
        INSERT INTO layers (layer_name, layer_type, schema_json, schema_version) VALUES
          ('サンプル圃場', 'polygon',
           '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true},{""key"":""crop"",""type"":""string"",""required"":false}]}'::jsonb, 1),
          ('サンプル観測点', 'point',
           '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true}]}'::jsonb, 1);
        INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
        SELECT layer_id, schema_version, schema_json, now(), NULL, 'system' FROM layers;";

    public static async Task RunAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var c = new NpgsqlCommand(TruncateSql, conn))
        {
            await c.ExecuteNonQueryAsync();
        }
        await using (var c = new NpgsqlCommand(LayersSeedSql, conn))
        {
            await c.ExecuteNonQueryAsync();
        }

        await SeedUsers.SeedAsync(connectionString);
    }
}
