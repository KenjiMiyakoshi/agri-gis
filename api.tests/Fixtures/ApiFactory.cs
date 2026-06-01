using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

    // テスト全体で共有する JWT 署名鍵 (HS256, 32+ bytes)。A502 の TokenForge と共有。
    public const string TestJwtSecret = "agri-gis-test-jwt-secret-32bytes!!__min__";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // api/Program.cs は環境変数 AGRI_GIS_DB を最優先するため、ここで差し込む
        Environment.SetEnvironmentVariable("AGRI_GIS_DB", _connectionString);
        // WA2 A201: JwtService が起動時に AGRI_GIS_JWT_SECRET を要求するため固定値を渡す。
        // 認証 middleware は WA3 (A204/A206) で配線されるまで未使用なので影響なし。
        Environment.SetEnvironmentVariable("AGRI_GIS_JWT_SECRET", TestJwtSecret);
        builder.UseSetting("ConnectionStrings:AgriGis", _connectionString);
    }
}
