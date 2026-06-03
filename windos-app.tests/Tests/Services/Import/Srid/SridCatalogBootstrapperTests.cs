using AgriGis.Desktop.Services.Import;
using AgriGis.Desktop.Services.Import.Srid;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Srid;

// C'206 (WC'2): SridCatalogBootstrapper の動作検証。
public sealed class SridCatalogBootstrapperTests
{
    private static IOptions<ImportOptions> Wrap(ImportOptions opts) => Options.Create(opts);

    [Fact]
    public void Bootstrap_RegistersValidWkt()
    {
        var converter = new SridConverter();
        var opts = new ImportOptions
        {
            SridCatalog = new()
            {
                new SridCatalogEntry
                {
                    Srid = 99001,
                    Name = "Test Tokyo Datum II",
                    Wkt = "PROJCS[\"Test\",GEOGCS[\"Tokyo\",DATUM[\"Tokyo\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",33],PARAMETER[\"central_meridian\",131],PARAMETER[\"scale_factor\",0.9999],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]"
                }
            }
        };

        var bootstrapper = new SridCatalogBootstrapper(Wrap(opts), converter);
        var result = bootstrapper.Bootstrap();

        Assert.Equal(1, result.Registered);
        Assert.Empty(result.Warnings);
        Assert.True(converter.IsSupported(99001));
    }

    [Fact]
    public void Bootstrap_InvalidEntry_AddsWarning_ContinuesOthers()
    {
        var converter = new SridConverter();
        var opts = new ImportOptions
        {
            SridCatalog = new()
            {
                // 不正: Srid=0
                new SridCatalogEntry { Srid = 0, Name = "Bad", Wkt = "x" },
                // 不正: WKT 空
                new SridCatalogEntry { Srid = 99002, Name = "Empty Wkt", Wkt = "" },
                // 正常
                new SridCatalogEntry
                {
                    Srid = 99003,
                    Name = "Good",
                    Wkt = "PROJCS[\"Good\",GEOGCS[\"Tokyo\",DATUM[\"Tokyo\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",33],PARAMETER[\"central_meridian\",131],PARAMETER[\"scale_factor\",0.9999],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]"
                }
            }
        };

        var bootstrapper = new SridCatalogBootstrapper(Wrap(opts), converter);
        var result = bootstrapper.Bootstrap();

        Assert.Equal(1, result.Registered);
        Assert.Equal(2, result.Warnings.Count);
        Assert.True(converter.IsSupported(99003));
        Assert.False(converter.IsSupported(0));
    }

    [Fact]
    public void Bootstrap_EmptyCatalog_NoOp()
    {
        var converter = new SridConverter();
        var opts = new ImportOptions { SridCatalog = new() };
        var bootstrapper = new SridCatalogBootstrapper(Wrap(opts), converter);
        var result = bootstrapper.Bootstrap();
        Assert.Equal(0, result.Registered);
        Assert.Empty(result.Warnings);
    }
}
