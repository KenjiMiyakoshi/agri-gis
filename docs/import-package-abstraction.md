# IImportPackage 抽象 (Phase C' C'1 / C'3)

Phase C で `ShapefilePackage` 単独で動かしていた sidecar 管理を `IImportPackage` 抽象に切り出し、MIF / TAB を同じ流儀で扱う。

## 背景

Phase C `ShapefilePackage`:
- zip 展開 → 一時 dir 配置 → `ShpPath` プロパティで `.shp` パス公開
- `IAsyncDisposable` で一時 dir 削除
- `.cpg` 等の任意 sidecar は `MissingOptional` プロパティで列挙

`GdalLayerSource` は `_package.ShpPath` 直参照しているため、MIF/TAB を扱うには両方の Package を受け取れる抽象が必要。

## 採用方針

### 最小 API 宣言

```csharp
namespace AgriGis.Desktop.Services.Import.Packages;

public interface IImportPackage : IAsyncDisposable
{
    /// <summary>SHP / MIF / TAB の主ファイルの絶対パス。GDAL OGR open に渡す。</summary>
    string PrimaryPath { get; }

    /// <summary>任意 sidecar (.cpg / .ind 等) のうち欠落しているもの。情報用、UI で警告表示。</summary>
    IReadOnlyList<string> MissingOptional { get; }
}
```

`Encoding`, `Srid`, `Driver` 等は **Package には含めない**。これらは:
- `IEncodingResolver` (CpgFile / UcsDetect)
- `ISridDetector` (Ogr / Manual / CoordSys 行)
- `GdalLayerSource` 内部の driver switch (`.mif` → `"MapInfo File"` 等)

の各責務に分離する。

### ShapefilePackage の段階移行

```csharp
public sealed class ShapefilePackage : IImportPackage
{
    public string ShpPath { get; }       // 後方互換 (既存テスト + 段階移行のため残置)
    public string PrimaryPath => ShpPath; // IImportPackage 実装
    public IReadOnlyList<string> MissingOptional { get; }
    public ValueTask DisposeAsync() => ...;
}
```

WC'1 末で `GdalLayerSource` が `_package.PrimaryPath` 参照に変わったら `ShpPath` を `[Obsolete]` マーク (WC'2 以降で削除候補)。

### MifPackage / TabPackage

```csharp
public sealed class MifPackage : IImportPackage
{
    public string PrimaryPath { get; }   // .mif の絶対パス
    public IReadOnlyList<string> MissingOptional { get; }  // .mid 等
    public string? CharSetHeader { get; }  // ".mif" ヘッダから抽出した CharSet 値
    public ValueTask DisposeAsync() => ...;
}

public sealed class TabPackage : IImportPackage
{
    public string PrimaryPath { get; }    // .tab の絶対パス
    public IReadOnlyList<string> MissingOptional { get; }  // .ind 等
    public string? CharSetHeader { get; }
    public string? CoordSysLine { get; }  // CoordSys 行を抽出 (SridDetector に渡す)
    public ValueTask DisposeAsync() => ...;
}
```

`CharSetHeader` / `CoordSysLine` は Package 独自プロパティで、抽象には含めない。`GdalLayerSource` は Package を `IImportPackage` 経由で扱いつつ、必要に応じて `is MifPackage` / `is TabPackage` でキャストして CharSet/CoordSys を取得する。

## 受入条件

1. `IImportPackage` interface が新規ファイルとして存在
2. `ShapefilePackage` が `IImportPackage` を実装し、`ShpPath` も後方互換で残置
3. `GdalLayerSource` が `_package.PrimaryPath` を使う (どの Package 経由でも動く)
4. 既存 Shapefile テスト全 green
5. MIF / TAB が同じ抽象経由で扱える (WC'1/WC'2 で実装)

## 関連

- `PHASE_C_PRIME_INDEX.md`
- `windos-app/Services/Import/Packages/ShapefilePackage.cs` (移行ベース)
- `windos-app/Services/Import/GdalLayerSource.cs` (`_package.PrimaryPath` 参照に変更)
