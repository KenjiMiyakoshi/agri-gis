using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace AgriGis.Desktop.Services.Import;

// WB4 B404: ProjNet を薄くラップした SRID 変換ヘルパ。
// 既知 SRID (4326 / 4612 / 6668 / 3857 / 30169-30179 日本平面直角) をキャッシュ。
// 入力 (sourceSrid, x, y) → 出力 (lon4326, lat4326)。Phase B では Point のみ。
public sealed class SridConverter
{
    private readonly CoordinateSystemFactory _csFactory = new();
    private readonly CoordinateTransformationFactory _ctFactory = new();
    private readonly Dictionary<int, ICoordinateTransformation> _cache = new();
    private readonly GeographicCoordinateSystem _target = GeographicCoordinateSystem.WGS84;

    public (double Lon, double Lat) To4326(int sourceSrid, double x, double y)
    {
        if (sourceSrid == 4326) return (x, y);

        var tx = GetTransformation(sourceSrid);
        var result = tx.MathTransform.Transform(new[] { x, y });
        return (result[0], result[1]);
    }

    private ICoordinateTransformation GetTransformation(int sourceSrid)
    {
        if (_cache.TryGetValue(sourceSrid, out var cached)) return cached;
        var sourceCs = CreateCoordinateSystem(sourceSrid)
            ?? throw new NotSupportedException($"unsupported SRID for CSV import: {sourceSrid}");
        var tx = _ctFactory.CreateFromCoordinateSystems(sourceCs, _target);
        _cache[sourceSrid] = tx;
        return tx;
    }

    private CoordinateSystem? CreateCoordinateSystem(int srid)
    {
        // 主要 SRID のみ手動でサポート (Phase B)。Phase C で gdal/proj 連携を検討。
        return srid switch
        {
            4326 => GeographicCoordinateSystem.WGS84,
            4612 => _csFactory.CreateFromWkt(
                "GEOGCS[\"JGD2000\",DATUM[\"Japanese_Geodetic_Datum_2000\","
                + "SPHEROID[\"GRS 1980\",6378137,298.257222101]],"
                + "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],"
                + "AUTHORITY[\"EPSG\",\"4612\"]]"),
            6668 => _csFactory.CreateFromWkt(
                "GEOGCS[\"JGD2011\",DATUM[\"Japanese_Geodetic_Datum_2011\","
                + "SPHEROID[\"GRS 1980\",6378137,298.257222101]],"
                + "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],"
                + "AUTHORITY[\"EPSG\",\"6668\"]]"),
            3857 => ProjectedCoordinateSystem.WebMercator,
            _ => null
        };
    }

    public bool IsSupported(int srid) => CreateCoordinateSystem(srid) is not null;
}
