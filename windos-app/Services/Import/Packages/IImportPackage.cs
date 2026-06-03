namespace AgriGis.Desktop.Services.Import.Packages;

// C'101 (WC'1): Shapefile / MIF / TAB に共通する Package 抽象。
//
// 最小 API:
//   - PrimaryPath: GDAL OGR open に渡す主ファイルの絶対パス
//   - MissingOptional: 任意 sidecar (.cpg / .mid / .ind 等) のうち欠落しているもの
//   - IAsyncDisposable: 一時 dir 再帰削除
//
// Encoding / Srid / Driver の責務は本 interface に持たせない (それぞれ
// IEncodingResolver / ISridDetector / GdalLayerSource 内 driver switch の責務)。
// 各 Package 固有プロパティ (ShapefilePackage.CpgPath, MifPackage.CharSetHeader 等) は
// 必要に応じて呼び出し側で `is ShapefilePackage shp` 等のキャストで取得する。
public interface IImportPackage : IAsyncDisposable
{
    /// <summary>SHP / MIF / TAB の主ファイルの絶対パス。GDAL OGR open に渡す。</summary>
    string PrimaryPath { get; }

    /// <summary>任意 sidecar のうち欠落しているもの。情報用、UI で警告表示。</summary>
    IReadOnlyList<string> MissingOptional { get; }
}
