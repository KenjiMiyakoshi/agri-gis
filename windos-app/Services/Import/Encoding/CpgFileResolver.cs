using AgriGis.Desktop.Services.Import.Packages;
using Microsoft.Extensions.Options;

namespace AgriGis.Desktop.Services.Import.Encoding;

// WC2 C105: IEncodingResolver の Phase C 唯一の実装。
//
// 解決順:
//   1. ShapefilePackage.CpgPath が存在 → ファイル内容を CpgFileParser.Parse で正規化
//   2. (1) が NULL なら ImportOptions.DefaultDbfEncoding (デフォルト "CP932")
//
// UI ComboBox 上書きは ViewModel で別経路 (Resolver は読み取り専用、Design 決定 5)。
// UcsDetectResolver は Phase D 申し送り (PHASE_C_DESIGN_P §6.13)。
public sealed class CpgFileResolver : IEncodingResolver
{
    private readonly IOptions<ImportOptions> _options;

    public CpgFileResolver(IOptions<ImportOptions> options)
    {
        _options = options;
    }

    public string Resolve(IImportPackage package)
    {
        var fallback = _options.Value.DefaultDbfEncoding;
        if (string.IsNullOrEmpty(fallback)) fallback = "CP932";

        // C'101 (WC'1): Shapefile 以外 (MIF/TAB) は CpgPath を持たないため fallback で返す。
        // MIF/TAB の CharSet ヘッダ経路は Phase C' WC'3 (UcsDetect + MifTabCharSetParser) で対応。
        if (package is not ShapefilePackage shp ||
            string.IsNullOrEmpty(shp.CpgPath) || !File.Exists(shp.CpgPath))
        {
            return fallback;
        }

        try
        {
            var raw = File.ReadAllText(shp.CpgPath);
            return CpgFileParser.Parse(raw) ?? fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
