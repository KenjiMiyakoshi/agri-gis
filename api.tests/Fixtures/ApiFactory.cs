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
    // F'401 (Phase F' WF'4): テストから追加 service 差し替えを注入できるフック
    private readonly Action<IServiceCollection>? _extraConfigure;

    public ApiFactory(string connectionString, Action<IServiceCollection>? extraConfigure = null)
    {
        _connectionString = connectionString;
        _extraConfigure = extraConfigure;
    }

    // D103 (WD1) テスト用: TokenForge / WithActorAs が user_sessions に INSERT する際に使う
    public string ConnectionString => _connectionString;

    // テスト全体で共有する JWT 署名鍵 (HS256, 32+ bytes)。A502 の TokenForge と共有。
    public const string TestJwtSecret = "agri-gis-test-jwt-secret-32bytes!!__min__";
    public const string TestJwtIssuer = "agri-gis-api";
    public const string TestJwtAudience = "agri-gis-windows";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // api/Program.cs は環境変数 AGRI_GIS_DB を最優先するため、ここで差し込む。
        // LGP106: 各 ApiFactory が独立した NpgsqlDataSource (既定 pool 上限 100) を持つため、
        //   1 テストランで多数の ApiFactory を生成すると Postgres の max_connections (既定 100) を
        //   超えて "53300: too many clients already" になる。テスト用 data source の pool 上限を
        //   小さく絞り、総接続数を抑える (本番接続文字列には影響しない)。
        var testConn = _connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            ? _connectionString
            : _connectionString.TrimEnd(';') + ";Maximum Pool Size=8";
        Environment.SetEnvironmentVariable("AGRI_GIS_DB", testConn);
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
            // F'401 (Phase F' WF'4): 個別テストからの追加差し替え (broker spy など)
            _extraConfigure?.Invoke(services);
        });
    }
}

// E501 (WE5): GeoServer 同期は常に成功扱い (テスト用)
internal sealed class FakeGeoServerStyleSync : IGeoServerStyleSync
{
    public Task<bool> PushStyleAsync(int layerId, string themeName, string sldXml, CancellationToken ct)
        => Task.FromResult(true);
}
