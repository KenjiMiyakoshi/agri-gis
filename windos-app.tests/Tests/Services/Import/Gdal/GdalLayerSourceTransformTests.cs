using System.IO.Compression;
using AgriGis.Desktop.Services.Import;
using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.Packages;
using AgriGis.Desktop.Services.Import.Srid;
using OSGeo.OGR;
using OSGeo.OSR;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Gdal;

// Phase C 実機 smoke test 5件目バグ回帰防止:
//   GdalLayerSource は ReadFeaturesAsync(4326) で source EPSG → 4326 への
//   座標変換を行わなければならない。EPSG:4326 の SHP しか smoke test して
//   いなかったため、Phase C 本体ではこの変換が抜けたまま実装されており、
//   日本平面直角系 (例: EPSG:2454 = JGD2000 / 第XII系 北海道北西部) の
//   SHP を投げると PostGIS 側で ST_Transform が
//   「latitude or longitude exceeded limits」を返して 500 になっていた。
[Collection(GdalCollection.Name)]
public sealed class GdalLayerSourceTransformTests
{
    private sealed class StubEncodingResolver : IEncodingResolver
    {
        public string Resolve(IImportPackage package) => "UTF-8";
    }

    [Fact]
    public async Task ReadFeaturesAsync_Epsg2454Source_ConvertsCoordinatesTo4326()
    {
        // 1. EPSG:2454 (平面直角XII系, 北海道北西部) で点 SHP を temp dir に作る。
        //    原点は (44°N, 142°15'E)。
        var tempSrc = Path.Combine(Path.GetTempPath(), $"shptest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempSrc);
        var basePath = Path.Combine(tempSrc, "fields");
        var zipPath = Path.Combine(tempSrc, "fields.zip");

        try
        {
            var driver = Ogr.GetDriverByName("ESRI Shapefile");
            Assert.NotNull(driver);

            using (var srs = new SpatialReference(""))
            {
                var imp = srs.ImportFromEPSG(2454);
                Assert.Equal(0, imp);
                srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

                using var ds = driver.CreateDataSource(basePath + ".shp", null);
                using var layer = ds.CreateLayer("fields", srs, wkbGeometryType.wkbPoint, null);
                using (var fd = new FieldDefn("name", FieldType.OFTString))
                {
                    fd.SetWidth(32);
                    layer.CreateField(fd, 1);
                }

                // 平面直角XII系の原点 (北行 0m, 東行 0m) → 緯度経度 (142°15'E, 44°N) 近辺
                using var feat = new Feature(layer.GetLayerDefn());
                feat.SetField("name", "origin");
                using (var pt = new Geometry(wkbGeometryType.wkbPoint))
                {
                    pt.AddPoint_2D(0, 0);
                    feat.SetGeometry(pt);
                }
                layer.CreateFeature(feat);
            }

            // 2. zip にまとめる (フラット展開可)
            using (var zfs = File.Create(zipPath))
            using (var arch = new ZipArchive(zfs, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.GetFiles(tempSrc, "fields.*"))
                {
                    if (Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    arch.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                }
            }

            // 3. ShapefilePackage + GdalLayerSource で読み出す
            await using var pkg = await ShapefilePackage.OpenAsync(zipPath, CancellationToken.None);
            await using var source = new GdalLayerSource(
                pkg,
                new ManualSridDetector(2454),
                new StubEncodingResolver());

            var features = new List<GeoJsonFeature>();
            await foreach (var f in source.ReadFeaturesAsync(4326, CancellationToken.None))
            {
                features.Add(f);
            }

            // 4. 1 点で coordinates が WGS84 範囲内 (平面直角IV系 原点近辺)
            Assert.Single(features);
            var coords = features[0].Geometry.GetProperty("coordinates");
            var lon = coords[0].GetDouble();
            var lat = coords[1].GetDouble();

            // 元コード (生 m 値で 0,0) が来る回帰なら lon=0, lat=0 になる。
            // 修正後は 平面直角XII系 原点 (142°15'E, 44°N) 近辺になる。
            Assert.InRange(lon, 141.5, 143.0);
            Assert.InRange(lat, 43.5, 44.5);
        }
        finally
        {
            try { Directory.Delete(tempSrc, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ReadFeaturesAsync_Source4326_NoOpTransform()
    {
        // source==4326 なら transform を組み立てない経路。座標は変わらない。
        var tempSrc = Path.Combine(Path.GetTempPath(), $"shptest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempSrc);
        var basePath = Path.Combine(tempSrc, "p");
        var zipPath = Path.Combine(tempSrc, "p.zip");

        try
        {
            var driver = Ogr.GetDriverByName("ESRI Shapefile");
            using (var srs = new SpatialReference(""))
            {
                srs.ImportFromEPSG(4326);
                srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
                using var ds = driver.CreateDataSource(basePath + ".shp", null);
                using var layer = ds.CreateLayer("p", srs, wkbGeometryType.wkbPoint, null);
                using (var fd = new FieldDefn("name", FieldType.OFTString)) { fd.SetWidth(8); layer.CreateField(fd, 1); }
                using var feat = new Feature(layer.GetLayerDefn());
                feat.SetField("name", "x");
                using var pt = new Geometry(wkbGeometryType.wkbPoint);
                pt.AddPoint_2D(143.2, 42.9); // 帯広付近
                feat.SetGeometry(pt);
                layer.CreateFeature(feat);
            }
            using (var zfs = File.Create(zipPath))
            using (var arch = new ZipArchive(zfs, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.GetFiles(tempSrc, "p.*"))
                {
                    if (Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    arch.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                }
            }

            await using var pkg = await ShapefilePackage.OpenAsync(zipPath, CancellationToken.None);
            await using var source = new GdalLayerSource(pkg,
                new ManualSridDetector(4326), new StubEncodingResolver());

            var list = new List<GeoJsonFeature>();
            await foreach (var f in source.ReadFeaturesAsync(4326, CancellationToken.None)) list.Add(f);

            Assert.Single(list);
            var coords = list[0].Geometry.GetProperty("coordinates");
            Assert.Equal(143.2, coords[0].GetDouble(), 4);
            Assert.Equal(42.9, coords[1].GetDouble(), 4);
        }
        finally
        {
            try { Directory.Delete(tempSrc, recursive: true); } catch { }
        }
    }
}
