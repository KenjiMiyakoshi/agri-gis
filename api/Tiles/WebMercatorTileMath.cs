namespace AgriGis.Api.Tiles;

// D201 (WD2): WebMercator (EPSG:3857) の z/x/y → BBOX 変換
// 標準 XYZ tile scheme (Google/OSM 系)
// z=0 で全球 1 タイル、z=N で 2^N x 2^N タイル
// 出力 BBOX は EPSG:3857 のメートル単位
public static class WebMercatorTileMath
{
    private const double EarthRadius = 6378137.0;          // WGS84 equatorial radius (m)
    private const double Origin = Math.PI * EarthRadius;   // = 20037508.342789244

    /// <summary>
    /// z/x/y タイル座標を EPSG:3857 の BBOX (minX, minY, maxX, maxY) に変換する。
    /// </summary>
    public static (double minX, double minY, double maxX, double maxY) TileToBbox3857(int z, int x, int y)
    {
        if (z < 0 || z > 30) throw new ArgumentOutOfRangeException(nameof(z), "z must be 0..30");
        long count = 1L << z;
        if (x < 0 || x >= count) throw new ArgumentOutOfRangeException(nameof(x), $"x must be 0..{count - 1} for z={z}");
        if (y < 0 || y >= count) throw new ArgumentOutOfRangeException(nameof(y), $"y must be 0..{count - 1} for z={z}");

        double tileSize = (2.0 * Origin) / count;
        double minX = -Origin + x * tileSize;
        double maxX = minX + tileSize;
        // Y は North-Up (y=0 が最北)
        double maxY = Origin - y * tileSize;
        double minY = maxY - tileSize;

        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// BBOX を WMS GetMap の bbox パラメタ文字列形式 (minX,minY,maxX,maxY) に整形。
    /// </summary>
    public static string FormatBboxArg(double minX, double minY, double maxX, double maxY) =>
        $"{minX:G17},{minY:G17},{maxX:G17},{maxY:G17}";
}
