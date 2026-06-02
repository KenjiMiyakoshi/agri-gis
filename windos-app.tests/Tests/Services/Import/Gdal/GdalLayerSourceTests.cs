using AgriGis.Desktop.Services.Import;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Gdal;

// WC4 C501: GdalLayerSource の単体テスト。
// 実 SHP ファイル fixture を使った E2E は phase-c-prime-followup で導入 (合成 SHP 生成スクリプト連携)。
// 本テストでは InternalsVisibleTo 経由で internal static 純粋関数を直接 assertion する。
[Collection(GdalCollection.Name)]
public sealed class GdalLayerSourceTests
{
    [Theory]
    [InlineData("CP932", "ENCODING=CP932")]
    [InlineData("UTF-8", "ENCODING=UTF-8")]
    [InlineData("EUC-JP", "ENCODING=EUC-JP")]
    public void BuildOpenOptions_NonEmpty_ReturnsEncodingPair(string encoding, string expected)
    {
        var opts = GdalLayerSource.BuildOpenOptions(encoding);
        Assert.Single(opts);
        Assert.Equal(expected, opts[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BuildOpenOptions_EmptyOrNull_ReturnsEmpty(string? encoding)
    {
        var opts = GdalLayerSource.BuildOpenOptions(encoding!);
        Assert.Empty(opts);
    }
}
