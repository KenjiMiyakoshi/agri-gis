using Npgsql;

namespace AgriGis.Api.Tests.Fixtures;

// 不変条件テスト用の行数カウント。
// 「INSERT 後 current=1, history=0, audit=+1」のような不変条件を検査する。
public sealed class RowCounters
{
    private readonly string _connStr;

    public RowCounters(string connectionString) => _connStr = connectionString;

    public Task<long> CountCurrentAsync(CancellationToken ct = default) =>
        CountAsync("feature_current", ct);
    public Task<long> CountHistoryAsync(CancellationToken ct = default) =>
        CountAsync("feature_history", ct);
    public Task<long> CountAuditAsync(CancellationToken ct = default) =>
        CountAsync("audit_log", ct);
    public Task<long> CountSchemaVersionAsync(CancellationToken ct = default) =>
        CountAsync("layer_schema_version", ct);
    public Task<long> CountLayersAsync(CancellationToken ct = default) =>
        CountAsync("layers", ct);

    public async Task<long> CountAsync(string table, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<int> MaxVersionAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT version FROM feature_current WHERE entity_id = @e", conn);
        cmd.Parameters.AddWithValue("e", entityId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? -1 : Convert.ToInt32(result);
    }
}
