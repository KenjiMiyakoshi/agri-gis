using System.Text;

namespace AgriGis.Desktop.Services.Import.Encoding;

// C'302 (WC'3): MIF/TAB の CharSet ヘッダ値を .NET Encoding 名に正規化する。
//
// MIF/TAB の CharSet 値は MapInfo 固有 (例: "WindowsJapanese", "WindowsLatin1")。
// 既存 IEncodingResolver の OGR Open option 値 ("CP932" など) と整合させるため
// マップ表で変換する。Unknown CharSet は null を返してフォールバックに委ねる。
public static class MifTabCharSetParser
{
    static MifTabCharSetParser()
    {
        // C'301/C'302 (WC'3): CP932 等の .NET Encoding 取得に必須。
        // 並列実行時に他テストより先に static ctor が走るよう、本クラスにも明示登録。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private static readonly IReadOnlyDictionary<string, string> CharSetMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Neutral"] = "ISO-8859-1",
            ["WindowsLatin1"] = "Windows-1252",
            ["WindowsLatin2"] = "Windows-1250",
            ["WindowsGreek"] = "Windows-1253",
            ["WindowsCyrillic"] = "Windows-1251",
            ["WindowsTurkish"] = "Windows-1254",
            ["WindowsHebrew"] = "Windows-1255",
            ["WindowsArabic"] = "Windows-1256",
            ["WindowsBaltic"] = "Windows-1257",
            ["WindowsVietnamese"] = "Windows-1258",
            ["WindowsTradChinese"] = "Big5",
            ["WindowsSimpChinese"] = "GB2312",
            ["WindowsJapanese"] = "CP932",
            ["WindowsKorean"] = "Windows-949",
            ["UTF-8"] = "UTF-8",
        };

    /// <summary>
    /// CharSet ヘッダ値 (例: "WindowsJapanese") を OGR Open option 形式の Encoding 名に正規化する。
    /// 不明な値は null を返す。
    /// </summary>
    public static string? ToEncodingName(string? charSetHeader)
    {
        if (string.IsNullOrEmpty(charSetHeader)) return null;
        return CharSetMap.TryGetValue(charSetHeader, out var name) ? name : null;
    }

    // ToEncoding は .NET Encoding 取得の environment 依存が大きいため public API には載せず、
    // 利用側が `Encoding.GetEncoding(ToEncodingName(x))` で必要に応じて取得する設計とする。
}
