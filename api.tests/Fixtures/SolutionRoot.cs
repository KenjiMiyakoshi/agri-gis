namespace AgriGis.Api.Tests.Fixtures;

// テストアセンブリの位置から、AgriGis.sln または .git を持つディレクトリを上に向かって探す。
// db/init/*.sql や db/migration/*.sql の相対パス解決に使う。
internal static class SolutionRoot
{
    private static string? _cached;

    public static string Find()
    {
        if (_cached is not null) return _cached;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AgriGis.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                _cached = dir.FullName;
                return _cached;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate solution root (AgriGis.sln or .git) from " + AppContext.BaseDirectory);
    }

    public static string Resolve(string relativePath) =>
        Path.GetFullPath(Path.Combine(Find(), relativePath));
}
