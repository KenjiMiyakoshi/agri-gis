namespace AgriGis.Desktop.Services.Import;

// WB4 B402: レイヤインポートの取り込み元抽象。
// 形式 (GeoJSON / CSV / Phase C SHP/MIF/TAB) の差を埋め、ImportWizard に統一インタフェースを提供。
public interface ILayerSource : IAsyncDisposable
{
    string SourceFormat { get; }      // "geojson" | "csv" | (Phase C で "shapefile" | "mif" | "tab")
    int? SourceSrid { get; }          // GeoJSON=4326 固定、CSV=NULL (ユーザ指定)、SHP=PRJ から検出

    Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct);

    // 取り込んだ feature を 4326 化して順次返す。
    // targetSrid は通常 4326 (API は 4326 GeoJSON を期待)。Phase C で他 SRID が要件化したら拡張。
    IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(int targetSrid, CancellationToken ct);
}
