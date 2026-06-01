using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGis.Api.Auth;
using AgriGis.Api.Endpoints;
using AgriGis.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    Environment.GetEnvironmentVariable("AGRI_GIS_DB")
    ?? builder.Configuration.GetConnectionString("AgriGis")
    ?? "Host=localhost;Port=5432;Database=agri_gis;Username=agri_user;Password=agri_pass";

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // DictionaryKeyPolicy はあえて設定しない：attributes のキーはユーザー定義なので、サーバ側で勝手にケース変換しない
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

const string CorsPolicy = "webgis";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

builder.Services.AddProblemDetails();

// JWT 基盤 (WA2/A201)。middleware 配線 (UseAuthentication/UseAuthorization) は WA3 (A204) で行う。
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PasswordHasher>();

var jwtBootstrap = new JwtService(builder.Configuration); // fail-fast: 起動時に secret 検証
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtBootstrap.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtBootstrap.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtBootstrap.SecretBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "login_id",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseCors(CorsPolicy);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGroup("/api/layers").MapLayerEndpoints();
app.MapGroup("/api/features").MapFeatureEndpoints();
app.MapGroup("/api/admin").MapAdminEndpoints();

app.Run();

// WebApplicationFactory<Program> から参照できるようにする (xUnit テスト用)
public partial class Program { }
