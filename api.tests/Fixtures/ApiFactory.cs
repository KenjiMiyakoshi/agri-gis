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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // api/Program.cs は環境変数 AGRI_GIS_DB を最優先するため、ここで差し込む
        Environment.SetEnvironmentVariable("AGRI_GIS_DB", _connectionString);
        builder.UseSetting("ConnectionStrings:AgriGis", _connectionString);
    }
}
