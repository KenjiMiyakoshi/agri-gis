using Npgsql;

namespace AgriGis.Api.Auth;

// F204 (Phase F WF2): ILayerPermissionService の本実装。
// admin role 持ちは即時 true (filter bypass)。それ以外は org_layer_permission を参照。
public sealed class LayerPermissionService : ILayerPermissionService
{
    private readonly NpgsqlDataSource _db;

    public LayerPermissionService(NpgsqlDataSource db)
    {
        _db = db;
    }

    public async Task<bool> CanViewAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (roles.Contains("admin")) return true;
        await using var cmd = _db.CreateCommand(
            "SELECT can_view FROM org_layer_permission WHERE org_id = @oid AND layer_id = @lid");
        cmd.Parameters.AddWithValue("oid", orgId);
        cmd.Parameters.AddWithValue("lid", layerId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is bool b && b;
    }

    public async Task<bool> CanEditAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (roles.Contains("admin")) return true;
        await using var cmd = _db.CreateCommand(
            "SELECT can_edit FROM org_layer_permission WHERE org_id = @oid AND layer_id = @lid");
        cmd.Parameters.AddWithValue("oid", orgId);
        cmd.Parameters.AddWithValue("lid", layerId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is bool b && b;
    }

    public async Task<IReadOnlyDictionary<int, (bool CanView, bool CanEdit)>> GetForOrgAsync(int orgId, CancellationToken ct)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT layer_id, can_view, can_edit FROM org_layer_permission WHERE org_id = @oid");
        cmd.Parameters.AddWithValue("oid", orgId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var map = new Dictionary<int, (bool, bool)>();
        while (await r.ReadAsync(ct))
        {
            map[r.GetInt32(0)] = (r.GetBoolean(1), r.GetBoolean(2));
        }
        return map;
    }
}
