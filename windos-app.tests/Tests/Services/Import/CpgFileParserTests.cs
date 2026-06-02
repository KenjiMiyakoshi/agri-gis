using AgriGis.Desktop.Services.Import.Encoding;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// WC4 C502: CpgFileParser.Parse 純粋関数の [Theory]
public sealed class CpgFileParserTests
{
    [Theory]
    [InlineData("CP932", "CP932")]
    [InlineData("cp932", "CP932")]
    [InlineData("Cp932", "CP932")]
    [InlineData("932", "CP932")]
    [InlineData("UTF-8", "UTF-8")]
    [InlineData("utf-8", "UTF-8")]
    [InlineData("UTF8", "UTF-8")]
    [InlineData("65001", "UTF-8")]
    [InlineData("EUC-JP", "EUC-JP")]
    [InlineData("eucjp", "EUC-JP")]
    [InlineData("EUCJP", "EUC-JP")]
    [InlineData("SJIS", "CP932")]
    [InlineData("Shift-JIS", "CP932")]
    [InlineData("shiftjis", "CP932")]
    public void Parse_KnownEncoding_ReturnsNormalized(string raw, string expected)
    {
        Assert.Equal(expected, CpgFileParser.Parse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void Parse_EmptyOrNull_ReturnsNull(string? raw)
    {
        Assert.Null(CpgFileParser.Parse(raw));
    }

    [Theory]
    [InlineData("CP932\n", "CP932")]
    [InlineData("CP932\r\n", "CP932")]
    [InlineData("  CP932  ", "CP932")]
    public void Parse_WhitespaceAndNewlines_AreStripped(string raw, string expected)
    {
        Assert.Equal(expected, CpgFileParser.Parse(raw));
    }

    [Fact]
    public void Parse_UnknownNumeric_DefaultsToCpN()
    {
        // 知らない CP 番号は "CP<n>" で返す (OGR に判断委譲)
        Assert.Equal("CP949", CpgFileParser.Parse("949"));
    }

    [Fact]
    public void Parse_UnknownString_PassesThroughUpper()
    {
        // 知らない文字列は大文字化して返す (OGR に渡す)
        Assert.Equal("KOI8-R", CpgFileParser.Parse("koi8-r"));
    }
}
