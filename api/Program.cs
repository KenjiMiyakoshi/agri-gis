using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    Environment.GetEnvironmentVariable("AGRI_GIS_DB")
    ?? builder.Configuration.GetConnectionString("AgriGis")
    ?? "Host=localhost;Port=5432;Database=agri_gis;Username=agri_user;Password=agri_pass";

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());

const string CorsPolicy = "webgis";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();
app.UseCors(CorsPolicy);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/layers", async (NpgsqlDataSource db) =>
{
    const string sql = @"
        SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at
        FROM layers
        ORDER BY layer_id";

    await using var cmd = db.CreateCommand(sql);
    await using var r = await cmd.ExecuteReaderAsync();

    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            layerId = r.GetInt32(0),
            layerName = r.GetString(1),
            layerType = r.GetString(2),
            ownerOrgId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
            isShared = r.GetBoolean(4),
            createdAt = r.GetDateTime(5)
        });
    }
    return Results.Ok(rows);
});

app.MapGet("/api/features", async (int layerId, NpgsqlDataSource db) =>
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

app.Run();
