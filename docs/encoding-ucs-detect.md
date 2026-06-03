# UcsDetectResolver (Phase C' C'5)

`UtfUnknown` NuGet で .dbf / MIF / TAB の文字コードを自動検出し、`CpgFileResolver` の前段に挟む。

## 背景

Phase C で `IEncodingResolver` interface 公開済、実装は `CpgFileResolver` (`.cpg` ファイル優先) のみ。UCSDet 系自動検出は **不採用** と明示し、Phase C' 候補に。

実運用では `.cpg` を含まない Shapefile が多く、CP932 (Shift_JIS) と Windows-1252 / EUC-JP / CP949 (Hangul) の判別が手動になりがち。

## 採用方針

### 案 A: NuGet `UtfUnknown` + 信頼度しきい値

NuGet `UtfUnknown` (Mozilla Universal Chardet の .NET port、Active メンテナンス、MIT):

```csharp
public sealed class UcsDetectResolver : IEncodingResolver
{
    private const float ConfidenceThreshold = 0.7f;
    private readonly IEncodingResolver _fallback;  // CpgFileResolver
    private readonly ILogger<UcsDetectResolver>? _logger;

    public Encoding? Resolve(IImportPackage package)
    {
        if (package is not ShapefilePackage shp) return _fallback.Resolve(package);
        var dbfPath = Path.ChangeExtension(shp.PrimaryPath, ".dbf");
        if (!File.Exists(dbfPath)) return _fallback.Resolve(package);

        // .dbf の先頭 4096 バイトを読む (header + 初期 record 数件)
        byte[] sample;
        using (var fs = File.OpenRead(dbfPath))
        {
            sample = new byte[Math.Min(4096, fs.Length)];
            fs.Read(sample, 0, sample.Length);
        }

        var result = CharsetDetector.DetectFromBytes(sample);
        var detected = result.Detected;
        if (detected is null || detected.Confidence < ConfidenceThreshold)
        {
            _logger?.LogInformation(
                "[UcsDetect] low confidence ({Confidence}) for {Path}, falling back",
                detected?.Confidence, dbfPath);
            return _fallback.Resolve(package);
        }

        _logger?.LogInformation(
            "[UcsDetect] detected {Encoding} (confidence={Confidence}) for {Path}",
            detected.EncodingName, detected.Confidence, dbfPath);
        return detected.Encoding;
    }
}
```

### DI 登録 (`Program.cs`)

UcsDetect → CpgFile → Default の chain:

```csharp
services.AddSingleton<CpgFileResolver>();
services.AddSingleton<IEncodingResolver>(sp =>
    new UcsDetectResolver(
        fallback: sp.GetRequiredService<CpgFileResolver>(),
        logger: sp.GetService<ILogger<UcsDetectResolver>>()));
```

`CpgFileResolver` がさらに `DefaultDbfEncoding` (CP932) にフォールバックする既存挙動は変えない。

### MIF/TAB の CharSet ヘッダ

MIF ファイル先頭の `CharSet "WindowsLatin1"` 行、TAB ファイル先頭の `CharSet "..."` 行から文字コードを抽出。

```csharp
public static class MifTabCharSetParser
{
    private static readonly IReadOnlyDictionary<string, string> CharSetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Neutral"] = "ISO-8859-1",
        ["WindowsLatin1"] = "windows-1252",
        ["WindowsLatin2"] = "windows-1250",
        ["WindowsGreek"] = "windows-1253",
        ["WindowsCyrillic"] = "windows-1251",
        ["WindowsTurkish"] = "windows-1254",
        ["WindowsHebrew"] = "windows-1255",
        ["WindowsArabic"] = "windows-1256",
        ["WindowsBaltic"] = "windows-1257",
        ["WindowsVietnamese"] = "windows-1258",
        ["WindowsTradChinese"] = "Big5",
        ["WindowsSimpChinese"] = "GB2312",
        ["WindowsJapanese"] = "Shift_JIS",
        ["WindowsKorean"] = "windows-949",
        ["UTF-8"] = "utf-8",
    };

    public static Encoding? Parse(string? charSetHeader)
    {
        if (string.IsNullOrEmpty(charSetHeader)) return null;
        if (!CharSetMap.TryGetValue(charSetHeader, out var encodingName)) return null;
        try { return Encoding.GetEncoding(encodingName); }
        catch { return null; }
    }
}
```

`MifPackage.CharSetHeader` / `TabPackage.CharSetHeader` から `MifTabCharSetParser.Parse` で `Encoding` を取り、UcsDetect より優先で採用。

### 優先順位の最終形

| Package | 解決順 |
|---------|--------|
| `ShapefilePackage` | UcsDetect → CpgFile → DefaultDbfEncoding (CP932) |
| `MifPackage` | CharSet ヘッダ → UcsDetect → DefaultDbfEncoding |
| `TabPackage` | CharSet ヘッダ → UcsDetect → DefaultDbfEncoding |

MIF/TAB はヘッダが信頼できる場合が多いため CharSet 最優先。SHP は `.cpg` の存在自体が任意なので UcsDetect を先に試す。

## 受入条件

1. NuGet `UtfUnknown` 追加 (windos-app.csproj)
2. `UcsDetectResolver` 新規 + DI chain 登録
3. `MifTabCharSetParser` 新規 + MIF/TAB Package と統合
4. CP932 / UTF-8 BOM / UTF-16 LE / EUC-JP / CP949 の 5 サンプル `.dbf` で UcsDetect が正しく判定
5. CharSet ヘッダ "WindowsJapanese" の MIF → `Shift_JIS` が採用される

## テスト

`UcsDetectResolverTests` (windos-app.tests):
- 5 文字コードの sample `.dbf` (16 バイトの header + 数件 record)
- 各々が期待 Encoding を返す
- 信頼度が低い (= 短い / ambiguous) sample → fallback CpgFileResolver
- MIF/TAB CharSet ヘッダパース (`MifTabCharSetParserTests`)

## 関連

- `PHASE_C_PRIME_INDEX.md`
- `windos-app/Services/Import/Encoding/CpgFileResolver.cs` (fallback)
- `windos-app/Services/Import/ImportOptions.cs` (DefaultDbfEncoding)
