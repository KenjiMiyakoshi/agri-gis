using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Gdal;

// WC4 C501 (テスタビリティ補強 C-2 採用): GdalBase.ConfigureAll() を Collection 単位で 1 回呼ぶ。
// xUnit の ICollectionFixture により、同 Collection 内のテストクラスは fixture を共有する。
public sealed class GdalFixture
{
    public GdalFixture()
    {
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
    }
}

[CollectionDefinition(Name)]
public sealed class GdalCollection : ICollectionFixture<GdalFixture>
{
    public const string Name = "Gdal";
}
