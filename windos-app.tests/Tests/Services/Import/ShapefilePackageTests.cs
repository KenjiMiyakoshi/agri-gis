using System.IO.Compression;
using AgriGis.Desktop.Services.Import.Packages;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// WC4 C501 / C103 検証: ShapefilePackage zip 展開ロジック。
// 中身はバイナリ的に valid な SHP である必要はない (ファイル名と存在の検証だけが対象)。
public sealed class ShapefilePackageTests
{
    private static string CreateTestZip(params (string name, string content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shp-test-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(path))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var es = entry.Open();
                using var sw = new StreamWriter(es);
                sw.Write(content);
            }
        }
        return path;
    }

    [Fact]
    public async Task OpenAsync_ValidZip_ReturnsAllPaths()
    {
        var zip = CreateTestZip(
            ("points.shp", "shp-binary"),
            ("points.shx", "shx-binary"),
            ("points.dbf", "dbf-binary"),
            ("points.prj", "GEOGCS[\"WGS 84\",...]"),
            ("points.cpg", "CP932"));
        try
        {
            await using var pkg = await ShapefilePackage.OpenAsync(zip, CancellationToken.None);
            Assert.EndsWith("points.shp", pkg.ShpPath);
            Assert.EndsWith("points.shx", pkg.ShxPath);
            Assert.EndsWith("points.dbf", pkg.DbfPath);
            Assert.EndsWith("points.prj", pkg.PrjPath);
            Assert.EndsWith("points.cpg", pkg.CpgPath);
            Assert.Empty(pkg.MissingOptionalExtensions);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task OpenAsync_NoPrj_RecordsAsMissing()
    {
        var zip = CreateTestZip(
            ("points.shp", "shp-binary"),
            ("points.shx", "shx-binary"),
            ("points.dbf", "dbf-binary"));
        try
        {
            await using var pkg = await ShapefilePackage.OpenAsync(zip, CancellationToken.None);
            Assert.Null(pkg.PrjPath);
            Assert.Null(pkg.CpgPath);
            Assert.Contains(".prj", pkg.MissingOptionalExtensions);
            Assert.Contains(".cpg", pkg.MissingOptionalExtensions);
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task OpenAsync_NoShp_ThrowsInvalidData()
    {
        var zip = CreateTestZip(("readme.txt", "no shapefile here"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ShapefilePackage.OpenAsync(zip, CancellationToken.None).AsTask());
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task OpenAsync_MultipleShp_ThrowsInvalidData()
    {
        var zip = CreateTestZip(
            ("points.shp", "a"),
            ("more.shp", "b"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ShapefilePackage.OpenAsync(zip, CancellationToken.None).AsTask());
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task OpenAsync_NonexistentZip_ThrowsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.zip");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ShapefilePackage.OpenAsync(path, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task OpenAsync_ZipSlip_Rejected()
    {
        var zip = Path.Combine(Path.GetTempPath(), $"slip-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // ".." を含むエントリ名で zip slip を試みる
            var entry = archive.CreateEntry("../evil/points.shp");
            using var es = entry.Open();
            using var sw = new StreamWriter(es);
            sw.Write("evil");
        }
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ShapefilePackage.OpenAsync(zip, CancellationToken.None).AsTask());
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task DisposeAsync_DeletesTempDir()
    {
        var zip = CreateTestZip(("points.shp", "x"));
        string? capturedPath;
        try
        {
            await using (var pkg = await ShapefilePackage.OpenAsync(zip, CancellationToken.None))
            {
                capturedPath = Path.GetDirectoryName(pkg.ShpPath);
                Assert.True(Directory.Exists(capturedPath));
            }
            Assert.False(Directory.Exists(capturedPath));
        }
        finally
        {
            File.Delete(zip);
        }
    }
}
