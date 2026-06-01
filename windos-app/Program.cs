using AgriGis.Desktop.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace AgriGis.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();
        // services.AddHttpClient<IApiClient, ApiClient>(...) は #35 (0503) で追加
        // services.AddTransient<MainForm>() などは #36 (0504) で実 UI が入ってから
        using var sp = services.BuildServiceProvider();

        Application.Run(new MainForm());
    }
}
