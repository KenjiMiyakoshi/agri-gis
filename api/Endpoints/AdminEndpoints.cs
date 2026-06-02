using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Json;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class AdminEndpoints
{
    // WB2 B201 (H2 解消): api/Json/JsonOpts.Default に集約済み

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        // 0207: PUT /api/admin/layers/{layerId}/schema
        // schema_json を差し替えて fn_layer_schema_upsert を呼ぶ。
        // X-Actor 必須、軽量バリデーションで key/type 空欄を 422。
        group.MapPut("/layers/{layerId:int}/schema",
            async (int layerId, UpdateSchemaRequestDto req, HttpContext ctx, ICurrentUser user, NpgsqlDataSource db) =>
        {
            var actor = user.DisplayName;
            var requestId = RequestContext.GetRequestId(ctx);

            var errors = new List<AttributeErrorDto>();
            if (req.Schema?.Fields is null)
            {
                errors.Add(new AttributeErrorDto("schema.fields", "required", "fields is required"));
            }
            else
            {
                for (var i = 0; i < req.Schema.Fields.Count; i++)
                {
                    var f = req.Schema.Fields[i];
                    if (string.IsNullOrWhiteSpace(f.Key))
                    {
                        errors.Add(new AttributeErrorDto($"schema.fields[{i}].key", "required", "key is required"));
                    }
                    if (string.IsNullOrWhiteSpace(f.Type))
                    {
                        errors.Add(new AttributeErrorDto($"schema.fields[{i}].type", "required", "type is required"));
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }

            var schemaJson = JsonSerializer.Serialize(req.Schema, JsonOpts.Default);

            await using var cmd = db.CreateCommand(
                "SELECT fn_layer_schema_upsert(@id, @s::jsonb, @a, @rid, @uid, @oid)");
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("s", schemaJson);
            cmd.Parameters.AddWithValue("a", actor);
            cmd.Parameters.AddWithValue("rid", requestId);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("oid", user.OrgId);

            var newVersion = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Ok(new UpdateSchemaResponseDto(layerId, newVersion));
        });

        return group;
    }
}
