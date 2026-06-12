using AgriGis.Api.Dto;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// LG102 (Phase LG WLG1): GET /api/layer-groups
// authenticated (RequireAuthorization は Program.cs の MapGroup 側)。
// 全グループのフラット一覧を返し、ツリー構築はクライアント側で行う。
public static class LayerGroupsEndpoints
{
    public static RouteGroupBuilder MapLayerGroupsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT group_id, parent_group_id, group_name, sort_order
                  FROM layer_group
                 ORDER BY sort_order, group_id";
            await using var cmd = db.CreateCommand(sql);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<LayerGroupDto>();
            while (await r.ReadAsync())
            {
                list.Add(new LayerGroupDto(
                    GroupId:       r.GetInt32(0),
                    ParentGroupId: r.IsDBNull(1) ? null : r.GetInt32(1),
                    GroupName:     r.GetString(2),
                    SortOrder:     r.GetInt32(3)));
            }
            return Results.Ok(list);
        });

        return group;
    }
}
