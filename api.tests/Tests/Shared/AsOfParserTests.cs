using AgriGis.Api.Errors;
using AgriGis.Api.Shared;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Shared;

// E501 (WE5): AsOfParser 単体テスト。Phase A FeatureEndpoints.ParseAsOf から共通化した動作の網羅。
public sealed class AsOfParserTests
{
    [Fact]
    public void Null_ReturnsNull()
    {
        Assert.Null(AsOfParser.TryParse(null));
    }

    [Theory]
    [InlineData("2025-01-01")]
    [InlineData("2026-06-03")]
    [InlineData("9999-12-31")]
    [InlineData("0001-01-01")]
    public void ValidDateOnly_Parses(string s)
    {
        var d = AsOfParser.TryParse(s);
        Assert.NotNull(d);
        Assert.Equal(s, d!.Value.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z")]  // ISO datetime
    [InlineData("2026/01/01")]            // slash 区切り
    [InlineData("01-01-2026")]            // 順序違い
    [InlineData("not-a-date")]
    [InlineData("")]
    public void InvalidFormat_ThrowsValidation(string s)
    {
        var ex = Assert.Throws<ValidationException>(() => AsOfParser.TryParse(s));
        Assert.Contains(ex.Errors, e => e.AttributeKey == "asOf" && e.Code == "format");
    }
}
