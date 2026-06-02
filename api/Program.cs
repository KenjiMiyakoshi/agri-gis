using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGis.Api.Auth;
using AgriGis.Api.Endpoints;
using AgriGis.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

// WB3 B203: バルク投入の設定 (MaxCountPerChunk / ChunkDefaultSize)
builder.Services.Configure<AgriGis.Api.Options.BulkInsertOptions>(
    builder.Configuration.GetSection(AgriGis.Api.Options.BulkInsertOptions.SectionName));

// D101/D201/D203 (WD1/WD2): GeoServer 接続設定 + HttpClient
builder.Services.Configure<AgriGis.Api.Options.GeoServerOptions>(
    builder.Configuration.GetSection(AgriGis.Api.Options.GeoServerOptions.SectionName));
builder.Services.AddHttpClient("geoserver", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<AgriGis.Api.Auth.IGeoServerStyleSync, AgriGis.Api.Auth.GeoServerStyleSync>();

// JWT 基盤 (WA2/A201)。middleware 配線 (UseAuthentication/UseAuthorization) は WA3 (A204) で行う。
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PasswordHasher>();

// D103 (WD1): user_sessions テーブル経由の JWT lifecycle 管理
builder.Services.AddScoped<IUserSessionStore, UserSessionStore>();

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

        // D103 (WD1): 検証通過後に user_sessions.deleted_at IS NULL を確認。
        // 既発行 (Phase A/B/C 期) token は sid_session claim を持たないため Fail で 401。
        // 同 token を持っていても logout 済なら 401。
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var raw = ctx.Principal?.FindFirst("sid_session")?.Value;
                if (string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var sessionId))
                {
                    ctx.Fail("missing sid_session claim (Phase D requires re-login)");
                    return;
                }
                var store = ctx.HttpContext.RequestServices.GetRequiredService<IUserSessionStore>();
                if (!await store.IsActiveAsync(sessionId, ctx.HttpContext.RequestAborted))
                {
                    ctx.Fail("session is invalidated (logout)");
                }
            }
        };
    });
builder.Services.AddAuthorization(o =>
{
    // 書き込み系 (POST/PATCH/DELETE /api/features) は admin または general
    o.AddPolicy("WriteFeature", p => p.RequireAuthenticatedUser().RequireRole("admin", "general"));
});

// A202: ICurrentUser DI
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// A203: 401/403 を ProblemDetails 形式で返す
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

// A207: 初期 admin upsert (テスト環境ではスキップ — fixture が seed する)
if (!IsTestEnvironment(builder))
{
    builder.Services.AddHostedService<InitialAdminBootstrap>();
}

var app = builder.Build();

// A204: middleware 順序 — CORS → Authentication → Authorization → RequestContext → ProblemDetails
app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// A206: ロールベース認可
app.MapGroup("/api/auth").MapAuthEndpoints();
app.MapGroup("/api/layers").MapLayerEndpoints().RequireAuthorization();
app.MapGroup("/api/features").MapFeatureEndpoints().RequireAuthorization();
var adminGroup = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole("admin"));
adminGroup.MapAdminEndpoints();
adminGroup.MapGroup("/organizations").MapAdminOrgsEndpoints();
adminGroup.MapGroup("/users").MapAdminUsersEndpoints();
adminGroup.MapGroup("/layers").MapAdminLayersEndpoints();
// D203 (WD2): admin theme CRUD (GET/PUT /api/admin/layers/{id}/style)
adminGroup.MapGroup("/layers").MapAdminLayerStyleEndpoints();

// D201 (WD2): GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png + selection overlay
//   tile は admin/general/guest 全員、selection は Bearer + owner
var tilesGroup = app.MapGroup("/tiles").RequireAuthorization();
tilesGroup.MapTilesEndpoints();
tilesGroup.MapTilesSelectionEndpoint();

// D202 (WD2): POST /api/selection + DELETE /api/selection/{sid}
app.MapGroup("/api/selection").MapSelectionEndpoints().RequireAuthorization();

app.Run();

static bool IsTestEnvironment(WebApplicationBuilder b) =>
    b.Environment.IsEnvironment("Testing")
    || string.Equals(Environment.GetEnvironmentVariable("AGRI_GIS_SKIP_BOOTSTRAP"), "1", StringComparison.Ordinal);

// WebApplicationFactory<Program> から参照できるようにする (xUnit テスト用)
public partial class Program { }
