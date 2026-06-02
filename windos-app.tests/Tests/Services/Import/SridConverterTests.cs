using AgriGis.Desktop.Services.Import;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// B504 (WB5): SridConverter 純粋関数テスト。
public sealed class SridConverterTests
{
    [Fact]
    public void To4326_From4326_IsIdentity()
    {
        var c = new SridConverter();
        var (lon, lat) = c.To4326(4326, 143.20, 42.91);
        Assert.Equal(143.20, lon);
        Assert.Equal(42.91, lat);
    }

    [Fact]
    public void To4326_FromJGD2011_KeepsRoughLocation()
    {
        // JGD2011 (EPSG:6668) は WGS84 と地理座標系として近似一致
        // (本来 cm オーダー差。テストでは 0.001° 未満を期待)
        var c = new SridConverter();
        var (lon, lat) = c.To4326(6668, 143.20, 42.91);
        Assert.InRange(Math.Abs(lon - 143.20), 0, 0.001);
        Assert.InRange(Math.Abs(lat - 42.91), 0, 0.001);
    }

    [Fact]
    public void IsSupported_KnownSrids_ReturnTrue()
    {
        var c = new SridConverter();
        Assert.True(c.IsSupported(4326));
        Assert.True(c.IsSupported(4612));
        Assert.True(c.IsSupported(6668));
        Assert.True(c.IsSupported(3857));
    }

    [Fact]
    public void IsSupported_UnknownSrid_ReturnsFalse()
    {
        var c = new SridConverter();
        Assert.False(c.IsSupported(99999));
    }

    [Fact]
    public void To4326_UnknownSrid_Throws()
    {
        var c = new SridConverter();
        Assert.Throws<NotSupportedException>(() => c.To4326(99999, 0, 0));
    }
}
