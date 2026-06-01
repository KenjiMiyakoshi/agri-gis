using AgriGis.Desktop.Forms;
using AgriGis.Desktop.Services;
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
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();

        services.AddHttpClient<IApiClient, ApiClient>(c =>
        {
            c.BaseAddress = new Uri(ApiBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        // BridgeMessenger は CoreWebView2 を受け取って Forms 側で new するため
        // ここでは DI 登録しない (#36 0504 で MainForm が直接 new する想定)。

        using var sp = services.BuildServiceProvider();

        Application.Run(new MainForm());
    }
}
