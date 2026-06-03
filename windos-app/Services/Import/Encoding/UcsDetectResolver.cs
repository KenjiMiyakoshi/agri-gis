using System.Diagnostics;
using AgriGis.Desktop.Services.Import.Packages;
using UtfUnknown;

namespace AgriGis.Desktop.Services.Import.Encoding;

// C'301 (WC'3): UTF.Unknown による文字コード自動検出。
// CpgFileResolver の前段に挟み、信頼度 ≥ 0.7 で採用、未満は fallback。
//
// 解決順 (Phase C' 採用方針、encoding-ucs-detect.md):
//   - ShapefilePackage: UcsDetect (.dbf 先頭 4096 byte) → CpgFile → Default
//   - MifPackage / TabPackage: CharSet ヘッダ → UcsDetect → Default
//     (MIF/TAB の CharSet 経路は ImportWizardViewModel 側で MifTabCharSetParser 経由)
public sealed class UcsDetectResolver : IEncodingResolver
{
    public const float ConfidenceThreshold = 0.7f;
    private readonly IEncodingResolver _fallback;

    public UcsDetectResolver(IEncodingResolver fallback)
    {
        _fallback = fallback;
    }

    public string Resolve(IImportPackage package)
    {
        // Shapefile のみ .dbf を読む経路。MIF/TAB は fallback (CharSet ヘッダ経路は別途)
        if (package is not ShapefilePackage shp ||
            string.IsNullOrEmpty(shp.DbfPath) ||
            !File.Exists(shp.DbfPath))
        {
            return _fallback.Resolve(package);
        }

        try
        {
            byte[] sample;
            using (var fs = File.OpenRead(shp.DbfPath))
            {
                var len = (int)Math.Min(4096, fs.Length);
                sample = new byte[len];
                fs.Read(sample, 0, len);
            }

            var result = CharsetDetector.DetectFromBytes(sample);
            var detected = result?.Detected;
            if (detected is null ||
                detected.Confidence < ConfidenceThreshold ||
                string.IsNullOrEmpty(detected.EncodingName))
            {
                Trace.WriteLine($"[UcsDetect] low confidence ({detected?.Confidence}) for {shp.DbfPath}");
                return _fallback.Resolve(package);
            }
            Trace.WriteLine($"[UcsDetect] detected {detected.EncodingName} (conf={detected.Confidence}) for {shp.DbfPath}");
            // OGR Open option / .cpg fallback で使われるため、Encoding 名は文字列のまま返す
            return NormalizeEncodingName(detected.EncodingName);
        }
        catch (IOException)
        {
            return _fallback.Resolve(package);
        }
        catch (UnauthorizedAccessException)
        {
            return _fallback.Resolve(package);
        }
    }

    // C'301: UTF.Unknown の EncodingName を OGR/.NET で扱いやすい形に正規化
    // (例: "Shift_JIS" → "CP932" は OGR 慣習に合わせるが、.NET には Encoding.GetEncoding("Shift_JIS") も
    //  動くため最小限の正規化に留める)
    private static string NormalizeEncodingName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "SHIFT_JIS" => "CP932",
            "WINDOWS-31J" => "CP932",
            "WINDOWS-1252" => "WINDOWS-1252",
            "ISO-8859-1" => "ISO-8859-1",
            _ => name
        };
    }
}
