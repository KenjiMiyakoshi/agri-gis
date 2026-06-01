using System.Text.Json.Nodes;
using Npgsql;

namespace AgriGis.Api.Endpoints;

public static class FeatureEndpoints
{
    public static RouteGroupBuilder MapFeatureEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (int layerId, NpgsqlDataSource db) =>
        {
            const string sql = @"
                SELECT
                    feature_id,
                    layer_id,
                    entity_id,
                    attributes,
                    ST_AsGeoJSON(ST_Transform(geom, 4326)) AS geom_json
                FROM feature_current
                WHERE layer_id = @layerId
                  AND geom IS NOT NULL";

            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("layerId", layerId);

            await using var r = await cmd.ExecuteReaderAsync();
            var features = new JsonArray();

            while (await r.ReadAsync())
            {
                var geomJson = r.GetString(4);
                var geometry = JsonNode.Parse(geomJson);

                var props = new JsonObject
                {
                    ["featureId"] = r.GetInt64(0),
                    ["layerId"] = r.GetInt32(1),
                    ["entityId"] = r.GetGuid(2).ToString()
                };

                if (!r.IsDBNull(3))
                {
                    var attrJson = r.GetString(3);
                    if (JsonNode.Parse(attrJson) is JsonObject attrObj)
                    {
                        foreach (var kv in attrObj)
                        {
                            if (!props.ContainsKey(kv.Key))
                                props[kv.Key] = kv.Value?.DeepClone();
                        }
                    }
                }

                features.Add(new JsonObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = geometry,
                    ["properties"] = props
                });
            }

            var fc = new JsonObject
            {
                ["type"] = "FeatureCollection",
                ["crs"] = new JsonObject
                {
                    ["type"] = "name",
                    ["properties"] = new JsonObject { ["name"] = "EPSG:4326" }
                },
                ["features"] = features
            };

            return Results.Content(fc.ToJsonString(), "application/geo+json");
        });

        return group;
    }
}
