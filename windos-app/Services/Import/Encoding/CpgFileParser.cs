namespace AgriGis.Desktop.Services.Import.Encoding;

// WC2 C105: .cpg ファイル内容を OGR Open option の ENCODING 値に正規化する純粋関数。
//
// 正規化ルール:
//   "CP932" / "cp932" / "Cp932"            → "CP932"
//   "932"                                  → "CP932" (Windows CodePage 932)
//   "UTF-8" / "utf-8" / "UTF8"             → "UTF-8"
//   "EUC-JP" / "euc-jp" / "eucjp"          → "EUC-JP"
//   ""    / null / 空白のみ                → null (呼び出し側で DefaultDbfEncoding fallback)
//   その他                                 → 入力の trim 結果 (OGR に判断委譲)
//
// .NET の Encoding ベース判定は意図的にしない (OGR が受け取る形式に合わせる)。
public static class CpgFileParser
{
    public static string? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('﻿'); // BOM 除去
        // 改行を除去 (.cpg は通常 1 行だが末尾改行のケースを救う)
        s = s.TrimEnd('\r', '\n').Trim();
        if (s.Length == 0) return null;

        // 数字のみ (例: "932" / "65001") を OGR が認識する形式に正規化
        if (s.All(char.IsDigit))
        {
            return s switch
            {
                "932" => "CP932",
                "65001" => "UTF-8",
                "20932" => "EUC-JP",
                _ => $"CP{s}"
            };
        }

        // 大文字/ハイフン/アンダースコアを揃える
        var upper = s.ToUpperInvariant().Replace("_", "-");
        return upper switch
        {
            "CP932" => "CP932",
            "UTF-8" or "UTF8" => "UTF-8",
            "EUC-JP" or "EUCJP" => "EUC-JP",
            "SHIFT-JIS" or "SHIFTJIS" or "SJIS" => "CP932",
            _ => upper
        };
    }
}
