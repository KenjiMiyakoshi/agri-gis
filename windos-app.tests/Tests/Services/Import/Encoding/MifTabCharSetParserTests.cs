using AgriGis.Desktop.Services.Import.Encoding;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Encoding;

// C'304 (WC'3): MifTabCharSetParser の マップ表検証。
public sealed class MifTabCharSetParserTests
{
    static MifTabCharSetParserTests()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    [Theory]
    [InlineData("WindowsJapanese", "CP932")]
    [InlineData("WindowsLatin1", "Windows-1252")]
    [InlineData("WindowsKorean", "Windows-949")]
    [InlineData("WindowsSimpChinese", "GB2312")]
    [InlineData("UTF-8", "UTF-8")]
    [InlineData("Neutral", "ISO-8859-1")]
    public void ToEncodingName_KnownCharSet_ReturnsExpected(string charSet, string expected)
    {
        Assert.Equal(expected, MifTabCharSetParser.ToEncodingName(charSet));
    }

    [Theory]
    [InlineData("windowsjapanese", "CP932")]
    [InlineData("WINDOWSJAPANESE", "CP932")]
    public void ToEncodingName_IsCaseInsensitive(string charSet, string expected)
    {
        Assert.Equal(expected, MifTabCharSetParser.ToEncodingName(charSet));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UnknownCharSet")]
    public void ToEncodingName_UnknownOrNull_ReturnsNull(string? charSet)
    {
        Assert.Null(MifTabCharSetParser.ToEncodingName(charSet));
    }

}
