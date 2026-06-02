// Phase C 実機 smoke test 用のサンプル Shapefile zip を生成する。
//
// 帯広付近の擬似圃場ポリゴンを 10 件生成し、CP932 dbf + WGS84 prj + cpg を
// 含めた zip にまとめる。
//
// usage: dotnet run --project tools/sample-shp-generator -- <output-zip-path>

using System.IO.Compression;
using MaxRev.Gdal.Core;
using OSGeo.OGR;
using OSGeo.OSR;

GdalBase.ConfigureAll();

var outputZip = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample-data", "fields_obihiro_shp.zip");
outputZip = Path.GetFullPath(outputZip);
Directory.CreateDirectory(Path.GetDirectoryName(outputZip)!);

Console.WriteLine($"Output: {outputZip}");

// 1) 一時ディレクトリに Shapefile セットを作る
var tempDir = Path.Combine(Path.GetTempPath(), $"shpgen-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
var basePath = Path.Combine(tempDir, "fields");
var shpPath = basePath + ".shp";

try
{
    var driver = Ogr.GetDriverByName("ESRI Shapefile");
    if (driver == null) throw new InvalidOperationException("ESRI Shapefile driver not found");

    using (var ds = driver.CreateDataSource(shpPath, null))
    {
        using var srs = new SpatialReference("");
        srs.ImportFromEPSG(4326);

        // ENCODING=CP932 を渡して dbf に日本語 (CP932) を書き込めるようにする
        using var layer = ds.CreateLayer("fields", srs, wkbGeometryType.wkbPolygon,
            new[] { "ENCODING=CP932" });

        // フィールド定義 (Shapefile DBF カラム名は 10 文字制限)
        using (var f1 = new FieldDefn("name", FieldType.OFTString)) { f1.SetWidth(32); layer.CreateField(f1, 1); }
        using (var f2 = new FieldDefn("crop", FieldType.OFTString)) { f2.SetWidth(16); layer.CreateField(f2, 1); }
        using (var f3 = new FieldDefn("area_ha", FieldType.OFTReal)) { layer.CreateField(f3, 1); }
        using (var f4 = new FieldDefn("active", FieldType.OFTInteger)) { f4.SetSubType(FieldSubType.OFSTBoolean); layer.CreateField(f4, 1); }
        using (var f5 = new FieldDefn("obs_date", FieldType.OFTString)) { f5.SetWidth(10); layer.CreateField(f5, 1); }

        // 帯広駅付近に 5 件 × 2 行 = 10 件の長方形圃場
        var crops = new[] { "じゃがいも", "小麦", "ビート", "とうもろこし", "大豆" };
        int idx = 0;
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                var lonW = 143.200 + col * 0.005;
                var lonE = lonW + 0.0045;
                var latS = 42.910 + row * 0.0035;
                var latN = latS + 0.0030;

                using var ring = new Geometry(wkbGeometryType.wkbLinearRing);
                ring.AddPoint_2D(lonW, latS);
                ring.AddPoint_2D(lonE, latS);
                ring.AddPoint_2D(lonE, latN);
                ring.AddPoint_2D(lonW, latN);
                ring.AddPoint_2D(lonW, latS);

                using var poly = new Geometry(wkbGeometryType.wkbPolygon);
                poly.AddGeometryDirectly(ring);

                using var feat = new Feature(layer.GetLayerDefn());
                feat.SetField("name", $"圃場 {(char)('A' + row)}{col + 1}");
                feat.SetField("crop", crops[idx % crops.Length]);
                feat.SetField("area_ha", 5.5 + idx * 0.7);
                feat.SetField("active", idx % 4 == 3 ? 0 : 1);
                feat.SetField("obs_date", $"2026-05-{15 + (idx % 10):D2}");
                feat.SetGeometry(poly);
                layer.CreateFeature(feat);
                idx++;
            }
        }
    }

    // 2) .cpg を CP932 で明示作成 (ジェネレータの dbf 内容も Windows ANSI = CP932 想定)
    File.WriteAllText(basePath + ".cpg", "CP932");

    // 3) zip にまとめる
    if (File.Exists(outputZip)) File.Delete(outputZip);
    using (var fs = File.Create(outputZip))
    using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
    {
        foreach (var f in Directory.GetFiles(tempDir))
        {
            archive.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
        }
    }

    var size = new FileInfo(outputZip).Length;
    Console.WriteLine($"Generated {outputZip} ({size:N0} bytes)");
    Console.WriteLine("Features: 10 polygons (帯広付近の擬似圃場)");
    Console.WriteLine("Fields  : name(string) crop(string) area_ha(number) active(boolean) obs_date(string→date 昇格)");
    Console.WriteLine("SRID    : EPSG:4326 (.prj 含む)");
    Console.WriteLine("Encoding: CP932 (.cpg 含む)");
}
finally
{
    try { Directory.Delete(tempDir, recursive: true); } catch { }
}
