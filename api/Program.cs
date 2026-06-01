using AgriGis.Api.Endpoints;
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

app.MapGroup("/api/layers").MapLayerEndpoints();
app.MapGroup("/api/features").MapFeatureEndpoints();
app.MapGroup("/api/admin").MapAdminEndpoints();

app.Run();
