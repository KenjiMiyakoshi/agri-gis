using AgriGis.Desktop.Services.Import.Packages;

namespace AgriGis.Desktop.Services.Import.Encoding;

// WC1 C102 で契約を確定、WC2 C105 で CpgFileResolver + CpgFileParser を実装。
//
// Phase C 設計の文字コードフロー (PHASE_C_DESIGN_P §6.10):
//   - 第 1 段: ShapefilePackage.CpgPath の .cpg ファイル内容を CpgFileParser でパース
//   - 第 2 段: 解決不能なら appsettings.json: Import:DefaultDbfEncoding (デフォルト CP932)
//   - 第 3 段: UI ComboBox 上書き (ViewModel 経路、IEncodingResolver は読み取り専用)
//
// Resolve 結果は GdalLayerSource が `Ogr.Open(path, new[] { $"ENCODING={resolved}" })`
// の Open オプションに渡す。プロセス環境変数 SHAPE_ENCODING は使用しない (xUnit 並列実行と
// 非互換、`PHASE_C_DESIGN_P` 実装リスクレビュー Design 決定 6)。
public interface IEncodingResolver
{
    /// <summary>
    /// .cpg ファイル内容 + appsettings 既定値 から文字コード名を確定する。
    /// 戻り値は OGR Open option 形式の文字列 (例: "CP932" / "UTF-8" / "EUC-JP")。
    /// </summary>
    string Resolve(ShapefilePackage package);
}
