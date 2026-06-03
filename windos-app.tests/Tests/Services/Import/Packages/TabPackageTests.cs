using System.IO.Compression;
using System.Text;
using AgriGis.Desktop.Services.Import.Packages;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Packages;

// C'206 (WC'2): TabPackage.OpenAsync の最小動作検証 (実 GDAL 非依存)。
public sealed class TabPackageTests : IDisposable
{
    private readonly string _tempDir;

    public TabPackageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tabpkg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task OpenAsync_FullSet_ExtractsCharSetAndCoordSys()
    {
        var zipPath = Path.Combine(_tempDir, "sample.zip");
        var tabContent = "!table\r\n" +
                         "!version 300\r\n" +
                         "!charset WindowsJapanese\r\n" +
                         "Definition Table\r\n" +
                         "  File \"sample.dat\"\r\n" +
                         "  Type NATIVE Charset \"WindowsJapanese\"\r\n" +
                         "  CharSet \"WindowsJapanese\"\r\n" +
                         "  CoordSys Earth Projection 8, 1000, \"m\", 133.5, 33, 0.9999, 0, 0\r\n";
        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "sample.tab", tabContent);
            await WriteEntryAsync(archive, "sample.map", "binary map");
            await WriteEntryAsync(archive, "sample.dat", "binary dat");
            await WriteEntryAsync(archive, "sample.id", "binary id");
            await WriteEntryAsync(archive, "sample.ind", "binary ind");
        }

        await using var pkg = await TabPackage.OpenAsync(zipPath, CancellationToken.None);

        Assert.NotEmpty(pkg.TabPath);
        Assert.NotNull(pkg.MapPath);
        Assert.NotNull(pkg.DatPath);
        Assert.NotNull(pkg.IdPath);
        Assert.NotNull(pkg.IndPath);
        Assert.Empty(pkg.MissingOptional);
        Assert.Equal("WindowsJapanese", pkg.CharSetHeader);
        Assert.StartsWith("CoordSys", pkg.CoordSysLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("133.5", pkg.CoordSysLine);

        // IImportPackage 抽象越し
        IImportPackage importPkg = pkg;
        Assert.Equal(pkg.TabPath, importPkg.PrimaryPath);
    }

    [Fact]
    public async Task OpenAsync_MissingInd_ReportsMissingOptional()
    {
        var zipPath = Path.Combine(_tempDir, "no-ind.zip");
        var tabContent = "!table\r\n  CharSet \"Neutral\"\r\n";

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "no-ind.tab", tabContent);
            await WriteEntryAsync(archive, "no-ind.map", "");
            await WriteEntryAsync(archive, "no-ind.dat", "");
            await WriteEntryAsync(archive, "no-ind.id", "");
        }

        await using var pkg = await TabPackage.OpenAsync(zipPath, CancellationToken.None);

        Assert.Null(pkg.IndPath);
        Assert.Contains(".ind", pkg.MissingOptional);
        Assert.Equal("Neutral", pkg.CharSetHeader);
    }

    [Fact]
    public async Task OpenAsync_NoTab_Throws()
    {
        var zipPath = Path.Combine(_tempDir, "nothing.zip");
        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "random.txt", "hello");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => TabPackage.OpenAsync(zipPath, CancellationToken.None).AsTask());
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        await using var es = entry.Open();
        await using var sw = new StreamWriter(es, Encoding.UTF8);
        await sw.WriteAsync(content);
    }
}
