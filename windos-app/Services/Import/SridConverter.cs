using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace AgriGis.Desktop.Services.Import;

// WB4 B404: ProjNet を薄くラップした SRID 変換ヘルパ。
// 既知 SRID (4326 / 4612 / 6668 / 3857) をハードコード switch でキャッシュ。
// 入力 (sourceSrid, x, y) → 出力 (lon4326, lat4326)。
//
// WC2 C106 (Phase C 拡張): RegisterWkt(int, string) で動的登録経路を追加。
// 和歌山旧測地系等のローカル CS WKT 本体収録は Phase C' 送り
// (appsettings.json: Import:SridCatalog[] 経路、PHASE_C_DESIGN_P §6.12)。
public sealed class SridConverter
{
    private readonly CoordinateSystemFactory _csFactory = new();
    private readonly CoordinateTransformationFactory _ctFactory = new();
    private readonly Dictionary<int, ICoordinateTransformation> _cache = new();
    private readonly Dictionary<int, CoordinateSystem> _registered = new();
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
        // 1. ハードコード (Phase B 既存 4 件)
        var hardcoded = srid switch
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
            3857 => (CoordinateSystem)ProjectedCoordinateSystem.WebMercator,
            _ => null
        };
        if (hardcoded is not null) return hardcoded;

        // 2. WC2 C106: 動的登録 (RegisterWkt 経由) のフォールバック
        return _registered.TryGetValue(srid, out var dynamic) ? dynamic : null;
    }

    public bool IsSupported(int srid) => CreateCoordinateSystem(srid) is not null;

    /// <summary>
    /// WC2 C106: WKT 文字列で SRID を動的登録する。重複登録は後勝ち。
    /// 不正 WKT は ProjNet の例外を呼び出し側に伝播。
    ///
    /// WKT 本体収録 (和歌山旧測地系等) は Phase C' で
    /// appsettings.json: Import:SridCatalog[] 経路に切り出される予定。
    /// Phase C 本体は API 公開 + ユニットテスト担保のみ。
    /// </summary>
    public void RegisterWkt(int srid, string wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt))
            throw new ArgumentException("WKT must not be empty", nameof(wkt));
        var cs = _csFactory.CreateFromWkt(wkt);
        _registered[srid] = cs;
        // 既存キャッシュを破棄 (重複登録の後勝ちセマンティクス保証)
        _cache.Remove(srid);
    }
}
