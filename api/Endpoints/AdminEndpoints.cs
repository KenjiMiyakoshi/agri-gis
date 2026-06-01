using System.Text.Json;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class AdminEndpoints
{
    // schema_json を JSONB として書き込む際の Serialize 用。
    // (#59 で導入された static JsonOpts と同じ設定。マージ後に統合してもよい。)
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        // 0207: PUT /api/admin/layers/{layerId}/schema
        // schema_json を差し替えて fn_layer_schema_upsert を呼ぶ。
        // X-Actor 必須、軽量バリデーションで key/type 空欄を 422。
        group.MapPut("/layers/{layerId:int}/schema",
            async (int layerId, UpdateSchemaRequestDto req, HttpContext ctx, NpgsqlDataSource db) =>
        {
            var actor = RequestContext.RequireActor(ctx);
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

            var schemaJson = JsonSerializer.Serialize(req.Schema, SerializerOptions);

            await using var cmd = db.CreateCommand(
                "SELECT fn_layer_schema_upsert(@id, @s::jsonb, @a, @rid)");
            cmd.Parameters.AddWithValue("id", layerId);
            cmd.Parameters.AddWithValue("s", schemaJson);
            cmd.Parameters.AddWithValue("a", actor);
            cmd.Parameters.AddWithValue("rid", requestId);

            var newVersion = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Ok(new UpdateSchemaResponseDto(layerId, newVersion));
        });

        return group;
    }
}
