namespace AgriGis.Desktop.Services.Import.Packages;

// WC1 C102 スケルトン (本実装は WC2 C103 で行う):
//   - zip → temp dir 実展開 (`/vsizip/` 不採用)
//   - .shp/.shx/.dbf/.prj/.cpg の存在検証 + 絶対パス公開
//   - `OpenAsync(zipPath, ct)` 静的ファクトリ
//   - IAsyncDisposable で再帰削除
//
// C103 で本ファイルを書き換える前提。C102 段階では `GdalLayerSource` がコンパイル可能
// であることを担保する最小型のみ。
public sealed class ShapefilePackage : IAsyncDisposable
{
    public string ShpPath { get; init; } = "";
    public string? PrjPath { get; init; }
    public string? CpgPath { get; init; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
