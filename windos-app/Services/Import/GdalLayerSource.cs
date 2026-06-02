using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.Packages;
using AgriGis.Desktop.Services.Import.Srid;

namespace AgriGis.Desktop.Services.Import;

// WC1 C102: `ILayerSource` (Phase B 確立) を実装する Shapefile/MIF/TAB 用 LayerSource の骨格。
//
// 設計判断 (PHASE_C_DESIGN_P §3 / §6):
//   - sourceFormat ctor 引数で driver 名を切替 (Phase C は "shapefile" のみ、
//     Phase C' で "MapInfo File" を渡せば MIF/TAB に再利用可能)
//   - 1 クラスに集約 (案 C 採用、Phase C' でも分割しない)
//   - 文字コードは IEncodingResolver で解決 → Ogr.Open(path, new[]{"ENCODING=..."}) で渡す
//     (環境変数 SHAPE_ENCODING 不使用、Design 決定 6)
//   - SRID 検出は ISridDetector に委譲、結果は SourceSrid プロパティと SridResolutionState に反映
//   - DisposeAsync で OGR DataSource → ShapefilePackage の連鎖解放
//
// 本ファイルは骨格のみ。実装の所在:
//   - InferSchemaAsync: C301 GdalInferenceStrategy (OFT → InferredField 写像)
//   - ReadFeaturesAsync: C302 OGR Geometry → GeoJSON 変換 + Multi 正規化
//
// internal static 純粋関数 (絶対パス組み立て等) は InternalsVisibleTo("AgriGis.Desktop.Tests")
// で test から assertion 対象になる予定 (C501)。
public sealed class GdalLayerSource : ILayerSource
{
    private readonly ShapefilePackage _package;
    private readonly ISridDetector _sridDetector;
    private readonly IEncodingResolver _encodingResolver;
    private readonly string _sourceFormat;

    // OGR DataSource は ReadFeaturesAsync / InferSchemaAsync で開かれる (C301/C302 で実装)。
    // SRID 検出も最初の使用時に走らせるため _detectedSrid は遅延初期化。
    // CS0649 抑制: C301/C302 で代入される予定。
#pragma warning disable CS0649
    private int? _detectedSrid;
    private SridResolutionState? _sridState;
#pragma warning restore CS0649

    public GdalLayerSource(
        ShapefilePackage package,
        ISridDetector sridDetector,
        IEncodingResolver encodingResolver,
        string sourceFormat = "shapefile")
    {
        _package = package;
        _sridDetector = sridDetector;
        _encodingResolver = encodingResolver;
        _sourceFormat = sourceFormat;
    }

    public string SourceFormat => _sourceFormat;

    /// <summary>
    /// 直近の SRID 検出結果。InferSchemaAsync / ReadFeaturesAsync 呼び出し前は null。
    /// `FallbackToWgs84` 採用時は 4326、`Rejected` / `FallbackToPrompt` 採用時は null。
    /// </summary>
    public int? SourceSrid => _detectedSrid;

    /// <summary>
    /// 直近の SRID 検出状態。ViewModel が `Next` 制御や `meta_jsonb.srid_inferred` 判定に使う。
    /// </summary>
    public SridResolutionState? SridState => _sridState;

    public Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct)
    {
        // 実装: C301 GdalInferenceStrategy.InferAsync(...) を呼ぶ。
        // OFT → InferredField 写像 + 100 feature サンプリングで nullable / date 再推定。
        throw new NotImplementedException("Implemented in WC2 C301 (GdalInferenceStrategy)");
    }

    public IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(int targetSrid, CancellationToken ct)
    {
        // 実装: C302 で OGR Geometry → GeoJSON 変換 + MultiPolygon/MultiLineString 固定 +
        //       Z/M 値 skip + WARN + IAsyncEnumerable で逐次 yield。
        // 呼び出し前に ISridDetector.DetectAsync で _detectedSrid / _sridState を確定する。
        throw new NotImplementedException("Implemented in WC2 C302 (Geometry → GeoJSON)");
    }

    public async ValueTask DisposeAsync()
    {
        // C301/C302 実装後はここで OGR DataSource を Dispose する。
        // 現時点では ShapefilePackage の連鎖解放のみ。
        await _package.DisposeAsync();
    }

    // ----- internal static 純粋関数群 (C501 のテスト対象) -----

    /// <summary>
    /// OGR Open option 形式の文字列を組み立てる (`ENCODING=CP932` 等)。
    /// </summary>
    internal static string[] BuildOpenOptions(string encoding)
    {
        if (string.IsNullOrEmpty(encoding))
        {
            return Array.Empty<string>();
        }
        return new[] { $"ENCODING={encoding}" };
    }
}
