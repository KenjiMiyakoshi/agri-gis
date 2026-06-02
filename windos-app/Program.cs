using System.Diagnostics;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Forms;
using AgriGis.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGis.Desktop;

internal static class Program
{
    // API のベース URL。本サイクルは開発用 5080 ハードコード。
    // 将来 appsettings.json や環境変数からの取得に拡張可。
    private const string ApiBaseUrl = "http://localhost:5080";

    [STAThread]
    private static void Main()
    {
        // WC1 C101: appsettings.json をロードし、Gdal:ConfigureOnStartup=true (既定) なら
        // GDAL を初期化する。Phase C Shapefile インポート (C102 GdalLayerSource) の前提条件。
        // Application.Run より先に呼ぶこと (OGR は process global state を持つため)。
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        if (configuration.GetValue("Gdal:ConfigureOnStartup", true))
        {
            MaxRev.Gdal.Core.GdalBase.ConfigureAll();
            Trace.WriteLine("[GDAL] ConfigureAll() done.");
            Console.WriteLine("[GDAL] ConfigureAll() done.");
        }
        else
        {
            Trace.WriteLine("[GDAL] ConfigureAll() skipped (Gdal:ConfigureOnStartup=false).");
        }

        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // A401: セッション保持 (in-memory)。A402 (LoginForm) でログイン成功時に Set される
        services.AddSingleton<ISessionStore, InMemorySessionStore>();

        // A403: 全 HTTP リクエストに Authorization: Bearer <token> を付与する DelegatingHandler
        services.AddTransient<BearerHandler>();

        services.AddHttpClient<IApiClient, ApiClient>(c =>
        {
            c.BaseAddress = new Uri(ApiBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<BearerHandler>();

        // BridgeMessenger は CoreWebView2 のライフサイクルが Form 依存のため
        // DI 登録せず、MainForm が WebView2 初期化完了後に new する。

        services.AddTransient<MainForm>();
        services.AddTransient<LoginForm>();
        // WB4 B406/B408
        services.AddTransient<LayerAdminForm>();
        services.AddTransient<ImportWizardForm>();

        using var sp = services.BuildServiceProvider();

        // A402: ログインダイアログを最初に表示。OK 以外で起動中止。
        using (var login = sp.GetRequiredService<LoginForm>())
        {
            if (login.ShowDialog() != DialogResult.OK)
            {
                return;
            }
        }

        Application.Run(sp.GetRequiredService<MainForm>());
    }
}
