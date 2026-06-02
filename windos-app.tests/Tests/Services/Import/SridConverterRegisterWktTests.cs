using AgriGis.Desktop.Services.Import;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// WC4 C502: SridConverter.RegisterWkt 動的登録 API のテスト。
public sealed class SridConverterRegisterWktTests
{
    // JGD2011 WKT (実在の EPSG:6668 と同一構造、テスト用に任意 SRID 99999 で登録)
    private const string Jgd2011Wkt =
        "GEOGCS[\"JGD2011\",DATUM[\"Japanese_Geodetic_Datum_2011\","
        + "SPHEROID[\"GRS 1980\",6378137,298.257222101]],"
        + "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],"
        + "AUTHORITY[\"EPSG\",\"6668\"]]";

    [Fact]
    public void RegisterWkt_NewSrid_IsSupportedAndUsable()
    {
        var c = new SridConverter();
        Assert.False(c.IsSupported(99999));

        c.RegisterWkt(99999, Jgd2011Wkt);

        Assert.True(c.IsSupported(99999));
        // 4326 への変換が例外なく走ること (恒等変換に近い、JGD2011 は WGS84 と地理座標的に近似)
        var (lon, lat) = c.To4326(99999, 143.20, 42.91);
        Assert.InRange(Math.Abs(lon - 143.20), 0, 0.001);
        Assert.InRange(Math.Abs(lat - 42.91), 0, 0.001);
    }

    [Fact]
    public void RegisterWkt_DuplicateSrid_OverwritesLastWins()
    {
        var c = new SridConverter();
        c.RegisterWkt(99999, Jgd2011Wkt);
        // もう一度同じ SRID に同じ WKT を登録しても例外なし (Dictionary 後勝ち)
        c.RegisterWkt(99999, Jgd2011Wkt);
        Assert.True(c.IsSupported(99999));
    }

    [Fact]
    public void RegisterWkt_InvalidWkt_ThrowsFromProjNet()
    {
        var c = new SridConverter();
        Assert.ThrowsAny<Exception>(() => c.RegisterWkt(99999, "this is not WKT"));
    }

    [Fact]
    public void RegisterWkt_EmptyWkt_Throws()
    {
        var c = new SridConverter();
        Assert.Throws<ArgumentException>(() => c.RegisterWkt(99999, ""));
    }

    [Fact]
    public void RegisterWkt_DoesNotAffectHardcoded()
    {
        var c = new SridConverter();
        // 4326 はハードコード経路、登録経路に影響されない
        c.RegisterWkt(99999, Jgd2011Wkt);
        Assert.True(c.IsSupported(4326));
        Assert.True(c.IsSupported(99999));
    }
}
