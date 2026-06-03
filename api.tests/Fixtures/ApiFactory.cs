using AgriGis.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGis.Api.Tests.Fixtures;

// WebApplicationFactory<Program> ベース。Program は api/Program.cs の partial。
// 接続文字列をコンテナ経由のものに差し替える。
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    // D103 (WD1) テスト用: TokenForge / WithActorAs が user_sessions に INSERT する際に使う
    public string ConnectionString => _connectionString;

    // テスト全体で共有する JWT 署名鍵 (HS256, 32+ bytes)。A502 の TokenForge と共有。
    public const string TestJwtSecret = "agri-gis-test-jwt-secret-32bytes!!__min__";
    public const string TestJwtIssuer = "agri-gis-api";
    public const string TestJwtAudience = "agri-gis-windows";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // api/Program.cs は環境変数 AGRI_GIS_DB を最優先するため、ここで差し込む
        Environment.SetEnvironmentVariable("AGRI_GIS_DB", _connectionString);
        // WA2 A201: JwtService が起動時に AGRI_GIS_JWT_SECRET を要求するため固定値を渡す。
        Environment.SetEnvironmentVariable("AGRI_GIS_JWT_SECRET", TestJwtSecret);
        // WA3 A207: InitialAdminBootstrap をテストではスキップ。seed は fixture 側で行う。
        Environment.SetEnvironmentVariable("AGRI_GIS_SKIP_BOOTSTRAP", "1");
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AgriGis", _connectionString);

        // E501 (WE5): IGeoServerStyleSync を fake に差し替え (テスト環境では GeoServer なし)
        // E'302 (WE'3): "geoserver" HttpClient も Fake handler に差し替え (Tiles テスト用)
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IGeoServerStyleSync, FakeGeoServerStyleSync>();
            services.AddHttpClient("geoserver")
                .ConfigurePrimaryHttpMessageHandler(() => new FakeGeoServerHandler());
        });
    }
}

// E501 (WE5): GeoServer 同期は常に成功扱い (テスト用)
internal sealed class FakeGeoServerStyleSync : IGeoServerStyleSync
{
    public Task<bool> PushStyleAsync(int layerId, string themeName, string sldXml, CancellationToken ct)
        => Task.FromResult(true);
}
