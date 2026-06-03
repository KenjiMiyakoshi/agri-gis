using System.IO.Compression;
using System.Text;
using AgriGis.Desktop.Services.Import.Packages;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Packages;

// C'105 (WC'1): MifPackage.OpenAsync の最小動作検証。
// テスト用に zip を生成して展開、ヘッダ抽出を確認する (実 GDAL に依存しない unit test)。
public sealed class MifPackageTests : IDisposable
{
    private readonly string _tempDir;

    public MifPackageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mifpkg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task OpenAsync_MifMidPair_ExtractsCharSetAndCoordSys()
    {
        var zipPath = Path.Combine(_tempDir, "sample.zip");
        var mifContent = "Version 300\r\n" +
                         "CharSet \"WindowsLatin1\"\r\n" +
                         "Delimiter \",\"\r\n" +
                         "CoordSys Earth Projection 1, 104\r\n" +
                         "Columns 1\r\n" +
                         "  Name Char(50)\r\n" +
                         "Data\r\n\r\n" +
                         "Point 100 200\r\n";
        var midContent = "\"Sample\"\r\n";

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "sample.mif", mifContent);
            await WriteEntryAsync(archive, "sample.mid", midContent);
        }

        await using var pkg = await MifPackage.OpenAsync(zipPath, CancellationToken.None);

        Assert.NotEmpty(pkg.MifPath);
        Assert.True(File.Exists(pkg.MifPath));
        Assert.NotNull(pkg.MidPath);
        Assert.True(File.Exists(pkg.MidPath!));
        Assert.Empty(pkg.MissingOptional);
        Assert.Equal("WindowsLatin1", pkg.CharSetHeader);
        Assert.StartsWith("CoordSys", pkg.CoordSysLine, StringComparison.OrdinalIgnoreCase);

        // IImportPackage 抽象越しの参照確認
        IImportPackage importPkg = pkg;
        Assert.Equal(pkg.MifPath, importPkg.PrimaryPath);
    }

    [Fact]
    public async Task OpenAsync_MissingMid_ReportsMissingOptional()
    {
        var zipPath = Path.Combine(_tempDir, "no-mid.zip");
        var mifContent = "Version 300\r\nCharSet \"Neutral\"\r\nData\r\n";

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "no-mid.mif", mifContent);
        }

        await using var pkg = await MifPackage.OpenAsync(zipPath, CancellationToken.None);

        Assert.Null(pkg.MidPath);
        Assert.Contains(".mid", pkg.MissingOptional);
        Assert.Equal("Neutral", pkg.CharSetHeader);
        Assert.Null(pkg.CoordSysLine);
    }

    [Fact]
    public async Task OpenAsync_NoMif_Throws()
    {
        var zipPath = Path.Combine(_tempDir, "nothing.zip");
        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "random.txt", "hello");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MifPackage.OpenAsync(zipPath, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task OpenAsync_MultipleMif_Throws()
    {
        var zipPath = Path.Combine(_tempDir, "multi-mif.zip");
        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "a.mif", "Version 300\r\n");
            await WriteEntryAsync(archive, "b.mif", "Version 300\r\n");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MifPackage.OpenAsync(zipPath, CancellationToken.None).AsTask());
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        await using var es = entry.Open();
        await using var sw = new StreamWriter(es, System.Text.Encoding.UTF8);
        await sw.WriteAsync(content);
    }
}
