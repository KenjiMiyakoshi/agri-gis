# agri-gis Phase C Design B — Shapefile 先行 / MIF・TAB 段階対応案

`PHASE_C_PLAN.md` の 15 論点に対し、**「Shapefile を Phase C 本体で完全対応、MIF/TAB は UI スタブのみで本実装は Phase C 続編 (C') へ送る」** 二段リリース方針で回答する。MITAB ドライバの安定性検証コストと、和歌山測地系等ローカル CS の整備コストを Phase C 本体から切り離して、まず日本市場で支配的な Shapefile を最短で出荷することを優先する。

## コアコンセプト

- **対応形式の段階化**: Phase C 本体 = Shapefile のみ完全対応。MIF/TAB は `ImportWizardForm` Step1 で選択肢として並べるが「Phase C 続編で対応 / 現在は Shapefile のみ」のラベルで非活性。実装着手は Phase C' (別 Issue) で MITAB ドライバ実機検証と一緒に
- **文字コード**: CP932 デフォルト + Step1 UI で UTF-8 / Shift_JIS / CP1252 を選択可能。`.cpg` 同梱時はそれを優先表示 (ユーザは上書き可)。UCSDet 等の自動判定は不採用 (誤判定リスクを取らない)
- **SRID 検出**: .prj を OGR `SpatialReference` でパース → `AuthorityCode` が EPSG として取れて `SridConverter.IsSupported` を通れば自動採用。失敗時は Step1 UI で **手動 SRID 指定を必須化** (空欄で Next 不可)。和歌山測地系のような EPSG 不所持 CS は Phase C' へ
- **zip 展開**: `ShapefilePackage` 専用ヘルパクラスを `Services/Import/Vfs/` に切り、temp dir へ実展開。OGR は実ファイル経路で開く。`/vsizip/` は採用しない (.cpg / .prj が圧縮内部にあるときの可搬性検証コストを避ける)
- **大規模 Shapefile**: OGR `Layer.GetNextFeature()` ループを `IAsyncEnumerable<GeoJsonFeature>` で 1 件単位 yield。Phase B `Chunker` が 1000 件に束ねるので追加実装不要
- **MaxRev.Gdal.WindowsRuntime.Minimal 同梱、x64 固定**。`MainForm.god class 分割`・`BulkInsertMaxCount 上限解除`・`fn_feature_bulk_insert 採用`は Phase C スコープ外を厳守

## 3 段リリース構成

### Phase C 本体で対応する範囲 (本 Design の対象)

- `windos-app.csproj` への GDAL NuGet (`MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal`) 追加 + x64 固定
- `Services/Import/Vfs/ShapefilePackage.cs` — zip→temp dir 展開、.shp/.shx/.dbf/.prj/.cpg 揃いチェック、`IDisposable` で temp dir 削除
- `Services/Import/GdalLayerSource.cs` — Shapefile 専用 (driver は `ESRI Shapefile` 決め打ち)。MIF/TAB 用 driver 分岐コードはコメントで TODO を残すが実装しない
- `Services/Import/InferenceStrategies/GdalInferenceStrategy.cs` — OGR `FieldDefn` → `InferredField` の純粋関数マッピング
- `Services/Import/SridConverter.cs` 拡張 — 既知 EPSG (4326/4612/6668/3857) のキャッシュ追加と `RegisterWkt(int srid, string wkt)` 動的登録 API のみ追加。ローカル CS WKT 本体の収録は Phase C'
- `ImportWizardForm` Step1 — Shapefile を活性化、ファイル選択 (zip 単体)、文字コード ComboBox、`.cpg` 検出時の自動セット、SRID 検出失敗時の手動入力欄
- `GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` — SHP サンプル 1 件 (CP932 属性名 + 4326 .prj) で契約自動検証
- Shapefile 1 万件 + 10 万件のメモリ・速度実測 (`BulkInsertMaxCount` 据え置きで成立するか確認)

### Phase C 続編 (C') で足す範囲 (本 Design はインタフェース面のみ確保)

- MIF/MID 対応 (`MapInfo File` driver) — `.mif` + `.mid` セット選択、`CharSet` 句パース
- TAB 対応 (`MapInfo File` driver の TAB 経路) — TAB zip 展開、MITAB ドライバの 64bit 安定性検証
- 和歌山測地系等ローカル CS の WKT 本体収録 (`SridConverter.RegisterWkt` を appsettings から呼ぶ実装)
- Step1 UI の MIF/TAB 活性化、形式別 options
- TAB / MIF の文字コード差異 (TAB は MITAB 既定 + `CharSet` 句、MIF はヘッダ `CharSet`) の `EncodingResolver` 分岐
- `GdalLayerSourceTests` への MIF/TAB サンプル追加

### Phase D 以降に送る範囲 (本 Design は触らない、申し送りのみ)

- 数百万件向けサーバラスタタイル化 (`scale-target-and-server-side-rendering`)
- 選択ハイライトのサーバ raster オーバーレイ化 (`selection-visualization-and-multi-select`)
- `BulkInsertMaxCount=5000` 上限解除と chunk サイズ最適化 (Phase B 申し送り、Phase C で実測後判断する案件)
- `fn_feature_bulk_insert` 専用関数 (Phase B 申し送り通り不採用継続)
- `MainForm` god class 分割 (H5、独立サイクル)
- ClickOnce / MSIX 差分配布

## 設計論点への回答 (Plan §設計論点 15 件)

1. **zip 展開の責務**: (c) 専用ヘルパ `Services/Import/Vfs/ShapefilePackage` で実 temp dir 展開。`/vsizip/` は採用しない。理由: .cpg / .prj が zip 内部にあるときの OGR 読み挙動を Phase C で深掘りしたくない / temp dir 経路なら既存の `using` パターンに乗る / TAB の zip 展開も Phase C' で同ヘルパに合流可能
2. **文字コード判定**: (b+c) `.cpg` 同梱優先 + Step1 ComboBox でユーザが上書き可能 (UTF-8 / CP932 / Shift_JIS / CP1252)。`.cpg` 不在時のデフォルトは CP932。自動推定 (UCSDet 等) は誤判定リスクを取らないため不採用
3. **SRID 検出失敗時のフォールバック**: (b) Step1 で **手動 SRID 指定を必須化**、空欄なら Next 不可。デフォルト 4326 黙認 (a) は属性は読めてもジオメトリが太平洋に飛ぶ事故を生むので不採用。`SourceSrid=null` で Step2 進めて Step3 直前確認 (c) は手戻りが大きい
4. **OGR FieldDefn → InferredField マッピング**: (a) OFT 型に完全準拠を Phase C 本体。OFTInteger/Integer64 → integer / OFTReal → number / OFTString → string / OFTDate/DateTime → date / OFTBinary は **未サポートとして InferSchema で除外** + WARN ログ。実値サンプリング再推定 (b) は CsvInferenceStrategy で実績はあるが GDAL の OGR Feature 走査を 2 周することになるので Phase D まで送る
5. **大規模 Shapefile のメモリ管理**: (a) `Layer.GetNextFeature()` を `IAsyncEnumerable<GeoJsonFeature>` で 1 件単位 yield、Phase B `Chunker` が 1000 件束ね。OGR Feature は yield スコープ内で `using` (Phase B の `await foreach` パターンと同じ)。SQL `LIMIT/OFFSET` ページング (b) は Shapefile では非効率
6. **GDAL ネイティブ DLL 配布**: (a) `MaxRev.Gdal.WindowsRuntime.Minimal` 同梱、配布 zip に直接含める。展開後数十 MB は許容範囲。インストーラ + 起動時ダウンロード (b) や ClickOnce 差分配布 (c) は Phase D 候補
7. **Step1 UI の有効化方法**: (a+b) Phase B の「Phase C 対応予定」ラベルを Shapefile からは削除して活性化、MIF/TAB は「Phase C 続編で対応」ラベルに変更して **ComboBox 上は表示するが選択時に非活性化**。Feature Flag (b) は導入しない (二段リリースを UI で明示する方が運用透明)
8. **ジオメトリ型混在の正規化**: (c) feature 単位で MultiPolygon / MultiLineString / MultiPoint に正規化。`feature_current.geom` が `geometry(Geometry, 3857)` 固定なので DB 影響なし。layers テーブルの `geometry_type` は **InferSchema 時に OGR `Layer.GetGeomType()` から決定し、混在 (`wkbUnknown`) なら GeometryCollection を avoid して Multi*** に昇格
9. **`BulkInsertMaxCount` 上限解除**: (a) Phase C スコープ外、据え置き 5000。Phase B 申し送りそのまま。10 万件 Shapefile の実測値を Phase C 完了レポートに添付し、Phase D で別 Issue 化
10. **ローカル SRS サポート方針**: Phase C 本体では (b) **EPSG コードを持つもののみ受け入れる**。EPSG 不所持の和歌山測地系等は **Phase C' で `SridConverter.RegisterWkt` 経由で収録**。本 Design では `RegisterWkt(int srid, string wkt)` API の追加だけ確保 (中身は Phase C' で適宜呼ぶ)
11. **SHP と TAB の文字コード差異**: Phase C 本体は SHP のみなので論点凍結。Phase C' で (c) 形式別 `EncodingResolver` を `Services/Import/Encoding/` 配下に切る前提でインタフェースだけ確保
12. **64bit/32bit 環境差**: (c) WinForms アプリ全体を `<PlatformTarget>x64</PlatformTarget>` 固定。`MaxRev.Gdal.WindowsRuntime.Minimal` が x64 のみ提供のため。AnyCPU + 実行時 native dll 切替 (b) は配布検証コストが高い
13. **部分的に読めない feature のエラーハンドリング**: (c) skip 上限 1% を超えたら fail-fast に切替、それ以下なら skip + WARN ログ。Step3 完了ダイアログにスキップ件数を表示。`appsettings.json` の `Import:SkipThresholdPercent=1.0` で調整可能
14. **OGR Geometry → GeoJSON 変換経路**: (a) OGR 標準 `Geometry.ExportToJson()` を採用。Phase B が `System.Text.Json` 直書きで GeoJSON を扱う方針なので整合的。NetTopologySuite 導入 (b) は依存増 + Z/M 扱い検証コストが Phase C 本体スコープ外
15. **`GdalBase.ConfigureAll()` 呼び出し位置**: (a) `Program.Main` 先頭で 1 回実行。起動コスト数百 ms は許容、`static` 状態を予測可能にする。`Lazy<>` 遅延 (b) はテスト並列実行で初回呼び出し競合が起きるリスクがあり不採用

## 主要クラス署名 (Phase C 本体)

```csharp
// Services/Import/Vfs/ShapefilePackage.cs
public sealed class ShapefilePackage : IDisposable
{
    public string ShpPath { get; }          // 展開後の .shp フルパス
    public string? CpgEncoding { get; }     // .cpg があれば中身 ("CP932" 等)、無ければ null
    public bool HasPrj { get; }
    public static ShapefilePackage OpenZip(string zipPath);  // temp dir 展開
    public void Dispose();                  // temp dir 削除
}

// Services/Import/GdalLayerSource.cs
public sealed class GdalLayerSource : ILayerSource
{
    public string SourceFormat => "shapefile";
    public int? SourceSrid { get; }         // .prj から検出した EPSG、不明なら ctor 引数の手動 SRID
    public GdalLayerSource(ShapefilePackage pkg, string encoding, int? manualSrid);
    public Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct);
    public IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(int targetSrid, CancellationToken ct);
    public ValueTask DisposeAsync();        // OGR DataSource 解放 + ShapefilePackage Dispose
}

// Services/Import/InferenceStrategies/GdalInferenceStrategy.cs
public static class GdalInferenceStrategy
{
    public static InferredField FromOgrField(OSGeo.OGR.FieldDefn defn);
    public static bool IsSupported(OSGeo.OGR.FieldType ogrType);  // OFTBinary は false
}

// Services/Import/SridConverter.cs (拡張点のみ)
public partial class SridConverter
{
    public void RegisterWkt(int srid, string wkt);     // Phase C で API のみ追加、Phase C' で活用
    public bool IsSupported(int srid);                  // 既存 + 動的登録分も対象
}
```

## テスト戦略

- `GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` を 1 ファイル追加するだけで Phase B 契約抽象 (InferSchema → ReadFeatures の同値性 / CT による中断 / Dispose 後の例外) が自動検証
- 追加 fixture: `windos-app.tests/Fixtures/import/shapefile/`
  - `points_4326_cp932.zip` (.shp/.shx/.dbf/.prj/.cpg、CP932 属性名 5 件)
  - `polygons_6668_utf8.zip` (JGD2011 / .cpg なし、UTF-8 指定で読む)
  - `large_10k_4326.zip` (10000 件、メモリ実測用)
  - `no_prj.zip` (SRID 手動指定経路の検証)
- 単体: `ShapefilePackageTests` (zip 展開 + Dispose で temp dir 削除確認 + .cpg パース)
- 単体: `GdalInferenceStrategyTests` (OFT 各型 → InferredField マッピング純粋関数)
- 統合: 既存 `ImportEndpointIntegrationTests` を SHP 経路で 1 ケース追加 (zip POST → Step3 完了 → feature_current に N 件)

## CI / 配布への影響

- CI ランナーは Windows のみ。Linux ランナー上では `GdalLayerSource` 系テストを `[Trait("Platform", "Windows")]` で除外
- `windos-app.csproj` の `<PlatformTarget>x64</PlatformTarget>` 化 — 既存テストプロジェクトも x64 に揃える必要があり、Phase B の x86/AnyCPU 設定との差分を C1 で確認
- 配布 zip サイズ: 現在 約 30MB → GDAL Native DLL 同梱で 約 80MB を想定。`docs/layer-import.md` に明記

## 既知のリスク / Phase C' への申し送り

- **MITAB ドライバの 64bit 安定性**: Phase C' で本格検証。`MaxRev.Gdal.WindowsRuntime.Minimal` の MITAB が和歌山測地系を含む旧日本測地系 TAB を読めるか実機サンプルが必要
- **和歌山測地系 WKT 入手**: Phase C' で `SridConverter.RegisterWkt` 経由で appsettings に登録する想定だが、正本となる WKT 文字列の出典 (測量法準拠 / 地理院 SRS など) を Phase C' Plan で明示
- **`BulkInsertMaxCount` 上限解除**: Phase C 実測値 (10 万件 Shapefile) を完了レポートに添付して Phase D で別 Issue 化
- **SHP zip 内部に他形式が混在するケース**: Phase C は単一 .shp 前提。複数 .shp を含む zip は Step1 で「複数 Shapefile を含む zip は未対応」エラー
