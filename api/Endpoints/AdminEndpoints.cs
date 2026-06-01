namespace AgriGis.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        // 後続イシュー #18 (0207: PUT /api/admin/layers/{layerId}/schema) で追加。
        return group;
    }
}
