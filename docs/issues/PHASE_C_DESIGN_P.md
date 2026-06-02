# Phase C Design 案 P — 採択案 (案 B ベース + 案 A の純粋 C# 分離 / 拡張 API / 並列耐性をマージ + 3 レビュー反映)

Phase C Design A/B/C の 3 案と、拡張性 / 実装リスク / テスタビリティの 3 レビューを統合した採択案。Phase C Issue 化フェーズの直接入力となる。

## 1. 採用ベースと選択理由

**ベース案 = 案 B (Shapefile 先行 / MIF・TAB は Phase C' 段階対応)**

選択理由:
- **3 レビュー横断で「致命点なし」が案 B のみ**: 実装リスクレビューは案 C を「`/vsizip/` + 4326 黙認 + WKT ハードコードの最大地雷原」と評価、案 A を「16.5 人日 + KML 先取り + `SourceConfirmDialog` lifetime」が重いとした。案 B は SRID 必須化のロックアウトと Phase C' 引き継ぎ債務が残るが、いずれもスコープ調整 + UX 妥協案で吸収できる
- **テスタビリティ 1 位**: `ILayerSourceContractTests` (Phase B で 1 形式 1 クラス継承パターン確立済) に最も素直に乗る。fixture も SHP 4 種で完結、ライセンス問題なし
- **Phase B Design P §9 申し送りとの整合**: 「`ENCODING=CP932` は Open オプション方式」「`fn_feature_bulk_insert` 不採用」「和歌山系実機検証 1 人日」を Phase B 採択時に確定済。案 B はこの 3 点を変更しない
- **MEMORY.md 整合**: バイテンポラル原則を変更せず、`stacked_pr_pitfall` (`base=main` 固定) を維持できる粒度

## 2. 案 A から取り込む部分 (純粋 C# 分離 / 拡張 API / 並列耐性)

「Phase C で投資しないと Phase D で破壊的になるもの」だけを案 A から先食いする。拡張性・テスタビリティ両レビューが共通で挙げた強み。

| 取り込み項目 | 案 A 由来 | 案 P での扱い |
|---|---|---|
| `IInferenceStrategy.GdalInferenceStrategy` を純粋関数化 | §6 OFT 写像表 | Phase B の `IInferenceStrategy` パターンに合流。`InferAsync(OgrLayerSnapshot): InferredField[]` の純関数。OGR モック不要なテスト粒度 |
| `SridConverter.RegisterWkt(int srid, string wkt) API` を Phase C 本体で公開 | 案 A §7 + 案 B 強み | 動的登録経路を Phase C 本体で確保。WKT 本体収録 (和歌山旧測地系) は Phase C' へ。テストは `RegisterWkt → IsSupported → ConvertToWgs84` の純粋関数テストで担保 |
| `CollectionFixture<GdalFixture>` で `GdalBase.ConfigureAll()` 1 回化 | 案 A §5 + テスタビリティ補強 C-2 | `[Collection("Gdal")]` 単純化 (案 C) は採用しない。`ICollectionFixture<GdalFixture>` で初回 1 回化、同一 Collection 内のみ逐次 |
| `IAsyncEnumerable<GeoJsonFeature>` 逐次 yield | 案 A コンセプト | Phase B の `ReadFeaturesAsync` が既に `IAsyncEnumerable`。10 万件耐性を Phase C 受け入れ基準に明記 (拡張性 補強 5) |
| x64 固定 (`<PlatformTarget>x64</PlatformTarget>`) + `GdalBase.ConfigureAll()` を `Program.Main` 先頭 | 案 A/C 共通 | `MaxRev.Gdal.WindowsRuntime.Minimal` が x64 のみ提供のため両論点同時確定 |

## 3. 案 C から取り込む部分

| 取り込み項目 | 案 C 由来 | 案 P での扱い |
|---|---|---|
| `GdalLayerSource` は 1 クラスに集約 (driver 切替は ctor 引数 `sourceFormat`) | 案 C §3 | 「Shapefile 専用クラス → MIF/TAB 専用クラス」の分割は Phase C' でも不要。OGR driver 名で switch するだけ |
| Step1 UI は Phase B の「Phase C 対応予定」ラベル解除 + ComboBox 活性化が中心、`SourceConfirmDialog` のモーダル追加はしない | 案 C §3 + テスタビリティ案 A 弱点 1 | Confirm 情報は Step1 inline 表示 + ViewModel プロパティ (`DetectedEncoding` / `DetectedSrid`) でヘッドレステスト可能化 |
| MIF/TAB の `geometry_type` 混在は feature 単位 `MultiPolygon/MultiLineString` 固定 | 案 C §2 論点 8 | 案 A の「Polygon 昇格」より単純。`feature_current.geom geometry(Geometry, 3857)` 制約と整合 |

## 4. 却下する部分 (理由付き)

| 却下項目 | 出典 | 理由 |
|---|---|---|
| KML を Phase C 内で対応 | 案 A コンセプト | テスタビリティ A-3 (KML fixture ライセンス) + 実装リスク A-1 (LIBKML が `Minimal` SKU に含まれない可能性)。Phase D 以降で再評価 |
| `OgrDriverRegistry` 抽象 + `ImportPackage` 抽象を Phase C 本体で導入 | 案 A §2 | 拡張性レビュー指摘の通り「形式 2-3 つしかない時点で抽象化は over-engineering」。Phase C は Shapefile のみ、抽象は Phase C' で必要になった時点で導入 |
| `SourceConfirmDialog` モーダル | 案 A §4 | テスタビリティ A-1 致命点 (`IDialogService` 抽象を Phase C で追加するコスト)。inline 表示 + ViewModel プロパティで代替 |
| UCSDet による文字コード自動推測 | 案 A §1 | 実装リスク A-3 (`.dbf` ASCII 部分で CP1252 と誤判定する anchoring bias)。`.cpg` 優先 + UI ComboBox 上書きに統一 |
| MIF/TAB を Phase C 本体に含める | 案 A コンセプト | 実装リスク A-1 (MITAB の `Minimal` SKU 同梱が未確認)。Phase C' 送りで Shapefile 1 形式に集中 |
| SRID 検出失敗時の「手動指定必須化 (空欄で Next 不可)」 | 案 B 原案 | 実装リスク B-2 (`.prj` 欠落 SHP を持ってきた現場ユーザの完全ロックアウト)。**3 値選択肢の設定駆動** (`appsettings.json: Import:SridFallbackPolicy`) で運用判断可能に |
| `/vsizip/` 仮想 FS で zip のまま開く | 案 C 論点 1 | 実装リスク C-1 (`.cpg` auto-detect が Windows 上でドライバ依存、サイレント文字化け)。**実 temp dir 展開** (`ShapefilePackage`) で統一 |
| 4326 黙認 (`.prj` 不在 SHP を無警告で 4326 として読む) | 案 C 論点 3 | 実装リスク C-2 (平面直角座標が地球の裏側にプロットされる致命事故)。設定駆動の `PromptUser` をデフォルトに |
| 和歌山旧測地系 WKT の `SridConverter` switch ハードコード | 案 C §3 | 実装リスク C-3 (TOWGS84 解釈差で過去データが事後座標ずれ)。WKT 本体収録は Phase C' で `appsettings.json` 経由 (案 A `srs-catalog.json` 路線) |
| CP932 固定 (UI 上書き不可) | 案 C 論点 2 | 拡張性レビュー (多言語データ流用時に契約破壊)。`.cpg` 優先 + UI ComboBox 上書きを採用 |
| MITAB ドライバ + LIBKML の同梱検証なしで Phase C 着手 | 案 A 前提 | 実装リスク Design 決定 1 (`Minimal` の `gdalinfo --formats` PoC を Phase C 着手前提条件化)。**0.5 人日の PoC を C0 として追加** |

## 5. 3 レビューの指摘への対応

### 5.1 拡張性レビュー

| 指摘 | 対応 |
|---|---|
| 補強 1: `ImportPackage` 抽象の段階導入 (案 A の重さ対策) | 採用。Phase C は `ShapefilePackage` 1 実装のみ、`IImportPackage` インタフェースは Phase C' で MIF/TAB 追加時に切る。**抽象化を Phase C で先取りしない**ことで 16.5 → 8〜10 人日に圧縮 |
| 補強 2: SRID フォールバックポリシーを設定駆動に | 採用。`appsettings.json: Import:SridFallbackPolicy = Reject \| PromptUser \| AssumeWgs84`、デフォルト `PromptUser`。**案 B のロックアウト** (Reject) と案 C のサイレント事故 (AssumeWgs84) を設定で切り替え可能に |
| 補強 3: `SridConverter.RegisterWkt` API を Phase C 本体で公開 | 採用。**API のみ公開、WKT 本体収録は Phase C'**。Phase C ではユニットテスト (動的登録 → IsSupported → ConvertToWgs84) で API 動作を担保。実装リスク B-3 (未使用 API merge 負債) は「API テストありの段階公開」で回避 |
| 補強 4: 文字コード検出を Strategy 化 (`IEncodingResolver` 切り出し) | **部分採用**。Phase C は `CpgFileResolver` 1 実装 + UI ComboBox 上書き経路のみ。`UcsDetectResolver` は Phase D 申し送り。`IEncodingResolver` インタフェース定義 + 1 実装の対で Phase C' UTF-16/EUC-JP/CP949 追加時にコスト 1 ファイル |
| 補強 5: 100 万 feature 耐性を受け入れ基準に明記 | 採用。`docs/layer-import.md` に「Phase C 完了条件: 10 万 feature SHP を import 完走 + メモリ上限 2GB 以下」を明記 (現実的閾値で 100 万 → 10 万に下方修正)。fixture は CI artifact ではなく生成スクリプト化 |
| 補強 6: WebGIS 側非影響の明文化 | 採用。`docs/layer-import.md` の Phase C 申し送りセクションに「WebGIS (GeoServer 等) は同 PostGIS テーブルを読むだけのため Phase C 設計と完全独立」を明記 |

### 5.2 実装リスクレビュー

| 指摘 | 対応 |
|---|---|
| 1. `MaxRev.Gdal.WindowsRuntime.Minimal` の MITAB + LIBKML 二重不確実性 | **C0 PoC を Phase C 着手前提条件化** (0.5 人日)。`gdalinfo --formats` で `ESRI Shapefile` の含有のみ確認できれば Phase C 本体着手可能。MITAB / LIBKML は Phase C' 着手前に再確認 |
| 2. `SourceConfirmDialog` の二重 OGR Open と temp dir 寿命 | **モーダル廃止** (案 C 路線採用)。Step1 inline 表示 + ViewModel プロパティで検出値を露出。OGR Open は `ShapefilePackage` 内で 1 回だけ実施し、`Layer.GetLayerDefn()` から `SpatialReference` / `FieldDefn` を取得 |
| 3. UCSDet 精度地雷 (`.dbf` ASCII 部分の CP1252 誤判定) | **UCSDet 不採用**。`.cpg` 優先 + ComboBox 上書きに統一。`.cpg` 不在時のデフォルトは `appsettings.json: Import:DefaultDbfEncoding=CP932` (運用調整可能) |
| 4. SRID 必須化のロックアウト | **3 値設定駆動** (Reject / PromptUser / AssumeWgs84) で吸収。デフォルト `PromptUser` (Step1 inline 警告 + Combo に手動 SRID 入力欄)。AssumeWgs84 採用時は `audit_log` に `srid_inferred=true` メタデータ記録 (Design 決定 2 の妥協案を採用) |
| 5. 未使用 `RegisterWkt` API merge 負債 | **API テスト付きで段階公開**。Phase C で `SridConverter.RegisterWkt(int, string)` を public API 化、ユニットテストで「動的登録 → `IsSupported(srid)` 通過 → `ConvertToWgs84` で恒等変換」を担保。WKT 本体収録 (Phase C') 時に proj.db 副作用は再評価 |
| 6. `/vsizip/` `.cpg` auto-detect のサイレント文字化け | **temp dir 実展開に統一**。`ShapefilePackage` で `.shp/.shx/.dbf/.prj/.cpg` を一括解凍、`IAsyncDisposable` で temp 削除。実装リスク Design 決定 3 に合致 |
| 7. 4326 黙認のサイレント破壊 | **デフォルト `PromptUser`** で防御。AssumeWgs84 を選択する運用でも `audit_log` メタデータで事後追跡可能 |
| 8. 和歌山旧測地系 WKT ハードコードによる事後座標ずれ | **WKT 本体収録は Phase C'**。Phase C は API のみ公開。Phase C' で `appsettings.json: Import:SridCatalog[]` 経路で外部設定化、リリース後の差し替えで過去データが座標ずれを起こさない構造に |
| Design 決定 1: `MaxRev.Gdal.WindowsRuntime.Minimal` 実機調査を C1 前 | **C0 PoC として明示計上** (0.5 人日) |
| Design 決定 2: SRID 検出失敗時の挙動を 3 案横並びで決め直し | **3 値設定駆動 + `_srid_inferred=true` audit メタデータ**で妥協案採用 |
| Design 決定 3: `/vsizip/` vs temp dir 実展開を全形式統一 | **temp dir 実展開で統一** (`ShapefilePackage` 1 実装) |
| Design 決定 4: `RegisterWkt` を「API のみ」or「本体収録込み」 | **API のみ Phase C 公開、本体収録は Phase C'**。テスト付き |
| Design 決定 5: `SourceConfirmDialog` を inline 表示に倒す | **採用**。案 A の C7 を 2.0 → 1.2 人日相当に圧縮 |
| Design 決定 6: xUnit 並列実行と GDAL static の統一表記 | **`ICollectionFixture<GdalFixture>`** で統一。`GdalBase.ConfigureAll()` は fixture 内で 1 回呼び。Collection 名は `"Gdal"` 固定 |

### 5.3 テスタビリティレビュー

| 指摘 | 対応 |
|---|---|
| A-1: `SourceConfirmDialog` モーダル不整合 (案 A) | **モーダル廃止**で解消。Step1 inline 表示 + ViewModel プロパティで Phase B B505 ヘッドレステストパターンと整合 |
| A-2: `ImportPackage` の `IAsyncDisposable` lifetime と契約テスト署名 | `ShapefilePackage` を `GdalLayerSource` の ctor 内で生成 + 内部保持 + `DisposeAsync` で連鎖解放。契約テスト基底の `CreateSource()` 同期署名は変更不要 |
| A-3: KML fixture ライセンス | **KML を Phase D 送り**で解消 |
| B-1: ComboBox 「非活性表示」の状態 ENUM | **Phase B の「Phase C 対応予定」ラベルを Phase C で活性化** = ENUM 不要。Phase C' で MIF/TAB を「Phase C' 対応予定」ラベル + 非活性で表示する設計は Phase C' Design で扱う |
| B-2: `EncodingResolver` インタフェースだけ確保 (テスト対象空白) | `CpgFileParser` 静的純粋関数を Phase C 本体で実装。`Parse(string raw): string?` を `[Theory]` (`"CP932"→"CP932"`, `"932"→"CP932"`, `""→null` 等) で網羅 |
| B-3: SRID 検出失敗時の ViewModel 集中 | **`ISridDetector` 抽象を切り、`OgrSridDetector` / `ManualSridDetector` / `EpsgFallbackDetector` の Strategy 化** (テスタビリティ B-3 補強採用)。`GdalLayerSource` は `ISridDetector` を ctor 注入で受け取り、Fake で SRID 検出失敗テスト可能 |
| C-1: `GdalLayerSource` private カプセル化でテスト不能 | **`InternalsVisibleTo("windos-app.tests")` を csproj に追加** (テスタビリティ C-1 補強)。`BuildShapefilePath` 等の純粋関数は internal static で公開、`/vsizip/` 不採用なので組み立て対象は temp dir パスのみ |
| C-2: `[Collection("Gdal")]` 並列性低下 | **`ICollectionFixture<GdalFixture>` 採用**で並列実行効率を維持 (テスタビリティ C-2 補強) |
| C-3: SRID 4326 黙認の ViewModel ENUM 切り出し | **`ImportWizardViewModel.SridResolutionState`** を `Detected / FallbackToPrompt / FallbackToWgs84 / Rejected` の 4 値で公開。ヘッドレステストで「.prj 不在 SHP → `FallbackToPrompt`」を assertion |
| 全案共通: `windos-app.tests` の TFM 分離 | **採用しない**。Phase B で `windos-app.tests` が既に Windows 専用化済 (Phase B Design P §5 で確認)。Phase C で TFM 再構成は不要 |
| 全案共通: 10 万件 fixture 生成スクリプト化 | 採用。`tests/fixtures/generate-large-shp.ps1` で起動時に temp に生成 → テスト後削除。git にコミットしない |

## 6. 採択案 案 P の完全な詳細

### 6.1 対応形式 (Phase C 本体)

| 形式 | 拡張子 | 入力経路 | 文字コード | SRID 検出 | Phase C スコープ |
|---|---|---|---|---|---|
| Shapefile | `.shp / .shx / .dbf / .prj / .cpg` (zip 同梱) | zip 1 ファイル選択 → temp dir 実展開 | `.cpg` 優先 + UI ComboBox 上書き + デフォルト `Import:DefaultDbfEncoding` | `.prj` → OGR `SpatialReference.AuthorityCode` → `SridConverter.IsSupported` | **対応** |
| MIF/MID | `.mif / .mid` | — | — | — | **Phase C' 送り** (Step1 「Phase C' 対応予定」ラベル + 非活性) |
| TAB | `.tab / .dat / .id / .map / .ind` | — | — | — | **Phase C' 送り** (同上) |
| KML | `.kml / .kmz` | — | — | — | **Phase D 送り** (Step1 表示なし) |

### 6.2 データモデル変更

**変更なし**。Phase B Design P 確立の `layers / feature_current / layer_import_job / audit_log` をそのまま使用。

理由:
- `layers.source_format` 列は Phase B で `CHECK (source_format IN ('geojson','csv','shapefile','mif','tab'))` 済 → 新規 enum 追加不要
- `layers.source_srid` 列は INT NULL 済 → SRID フォールバック 3 値とも書き込み可能
- `audit_log.meta_jsonb` (Phase B 既存) に `srid_inferred=true` を追記する経路を `fn_feature_insert` 呼び出し時の `p_meta_json` 引数で実現 (関数シグネチャ変更なし)

### 6.3 API endpoint

**変更なし**。Phase B 確立の `POST /api/admin/layers/{id}/features:bulk` を WinForms から呼ぶだけ。

唯一の差分: WinForms 側で `BulkFeaturesRequestDto.sourceFormat` を `"shapefile"` で送る (Phase B の enum に追加済)。

### 6.4 PL/pgSQL 関数の追加

**追加なし**。Phase B 確立の `fn_feature_insert` / `fn_layer_create` / `fn_layer_delete` をそのまま使用。

`p_meta_json` (Phase B から `JSONB NULL` で受け取り) に `{"srid_inferred": true, "srid_fallback_policy": "AssumeWgs84"}` を入れる経路を WinForms 側で組み立てる (DB 関数側は受け取って `audit_log.meta_jsonb` に格納するだけ、変更ゼロ)。

### 6.5 WinForms UI フロー

Phase B `ImportWizardForm` の **Step1 を拡張**するのみ。Step2/Step3 は変更なし。

```
ImportWizardForm (既存)
 ├─ Step1: SourceFormatPicker (Phase C で SHP を活性化)
 │   ├─ ComboBox 項目:
 │   │   ├─ GeoJSON (Phase B 既存)
 │   │   ├─ CSV (Phase B 既存)
 │   │   ├─ Shapefile ZIP ← Phase C で活性化 (このセクションを以下に詳述)
 │   │   ├─ MapInfo MIF/MID (Phase C' 対応予定) ← 非活性
 │   │   └─ MapInfo TAB (Phase C' 対応予定) ← 非活性
 │   └─ Shapefile 選択時の inline 表示 (モーダル不使用):
 │       ├─ ファイル選択ボタン → zip パス
 │       ├─ [自動検出] ボタン
 │       │   └─ ShapefilePackage.OpenAsync(zipPath) → temp 展開 → OGR Open (1 回)
 │       │       → ViewModel に DetectedEncoding / DetectedSrid / FieldCount / FeatureCount を露出
 │       ├─ TextBox: 検出文字コード (`.cpg` から / 編集可能 ComboBox)
 │       ├─ TextBox: 検出 SRID (空欄なら SridFallbackPolicy に従う)
 │       └─ ViewModel.SridResolutionState ∈ { Detected, FallbackToPrompt, FallbackToWgs84, Rejected }
 │           → Rejected なら Next 不可、FallbackToPrompt なら手動入力欄で SRID 確定
 ├─ Step2: SchemaGrid (Phase B 既存、変更なし)
 └─ Step3: 投入実行 (Phase B 既存、変更なし)
     └─ ProjNet で 4326 化 → 1000 件 chunk → POST .../features:bulk × N
        (`sourceFormat="shapefile"` で送信、SRID 未検出時は `p_meta_json` 経由で audit メタデータ記録)
```

ViewModel ヘッドレステストで `SridResolutionState` / `DetectedEncoding` を assertion 可能。

### 6.6 GDAL 依存と配布戦略

| 項目 | 採択値 | 理由 |
|---|---|---|
| パッケージ | `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` | Phase B 申し送り通り |
| 配布サイズ | 約 60〜80MB (展開後) | `Minimal` SKU、配布 zip に直接含める |
| `GdalBase.ConfigureAll()` 呼び出し位置 | `Program.Main` 先頭 (起動 200〜500ms 増、許容) | `Lazy<>` 遅延は xUnit 並列で static 汚染リスク |
| `PlatformTarget` | x64 固定 (`<PlatformTarget>x64</PlatformTarget>`) | `Minimal` SKU が x64 のみ提供 |
| 文字コード指定 | `Ogr.Open(path, new[] { "ENCODING=CP932" })` 形式の Open オプション | プロセス環境変数 `SHAPE_ENCODING` は xUnit 並列と非互換 |
| zip 展開 | temp dir 実展開 (`ShapefilePackage`、`IAsyncDisposable` で自動削除) | `/vsizip/` は `.cpg` auto-detect 不安定 |
| C0 PoC | Phase C 着手前提条件として 0.5 人日計上 | `gdalinfo --formats` で `ESRI Shapefile` 含有確認 |

### 6.7 スキーマ推論ロジック

`GdalInferenceStrategy : IInferenceStrategy` を追加。OGR `FieldDefn` → `InferredField` の純粋関数写像。

| OGR OFT 型 | InferredField.type | nullable | 備考 |
|---|---|---|---|
| OFTInteger | `"integer"` | OFT は null 区別なし、サンプル値で再推定 | |
| OFTInteger64 | `"integer"` | 同上 | |
| OFTReal | `"number"` | 同上 | |
| OFTString | `"string"` | 空文字 → nullable=true 候補 | サンプル値で ISO8601 date 試行 |
| OFTDate | `"date"` | 同上 | |
| OFTDateTime | `"date"` | 同上 | |
| OFTBinary | (skip) | — | サポートせず警告ログ |
| OFTStringList | `"string"` | — | JSON 配列文字列化 |
| OFTIntegerList | `"string"` | — | 同上 |

```csharp
class GdalInferenceStrategy : IInferenceStrategy {
    public string SourceFormat => "shapefile";
    public Task<IReadOnlyList<InferredField>> InferAsync(Stream input, CancellationToken ct);
    // input は ShapefilePackage 展開後の .shp パス情報を持つラッパ
    // 内部で OGR Open + GetLayerDefn + FieldDefn ループ + 100 feature サンプリングで nullable/型補正
}
```

### 6.8 `ILayerSource` 実装 (`GdalLayerSource`)

```csharp
sealed class GdalLayerSource : ILayerSource {
    public string SourceFormat { get; }                    // "shapefile" (Phase C')
    public int? SourceSrid { get; }                         // 検出値 or 注入された ManualSrid or null
    private readonly ShapefilePackage _package;
    private readonly ISridDetector _sridDetector;
    private readonly IEncodingResolver _encodingResolver;

    public GdalLayerSource(
        ShapefilePackage package,
        ISridDetector sridDetector,
        IEncodingResolver encodingResolver,
        string sourceFormat = "shapefile") { ... }

    public Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct);
    public async IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(
        int sourceSrid, [EnumeratorCancellation] CancellationToken ct) {
        // OGR Layer.GetNextFeature() ループ → Geometry.ExportToJson() → properties JSONB
        // yield return で 1 件単位、Phase B Chunker が 1000 件束ね
    }
    public ValueTask DisposeAsync(); // ShapefilePackage の temp dir 削除を連鎖
}
```

### 6.9 `SridConverter` 拡張

```csharp
class SridConverter {
    // Phase B 既存
    public bool IsSupported(int srid);
    public CoordinateTransformation GetTransformation(int from, int to);

    // Phase C 新規 (API のみ、WKT 本体収録は Phase C')
    public void RegisterWkt(int srid, string wkt);
    // → 内部 dictionary に追加、以降 IsSupported(srid) が true を返す
}
```

ユニットテスト: 動的登録 → `IsSupported(srid)` 通過 → 恒等変換 (`from==to`) で例外なし。WKT 本体収録は Phase C' で `appsettings.json: Import:SridCatalog[{srid, wkt}]` 経路で外部設定化。

### 6.10 設定値 (`appsettings.json`)

```json
{
  "Import": {
    "SridFallbackPolicy": "PromptUser",   // Reject | PromptUser | AssumeWgs84
    "DefaultDbfEncoding": "CP932",
    "BulkInsertMaxCount": 5000             // Phase B 据え置き
  },
  "Gdal": {
    "ConfigureOnStartup": true             // false で C0 PoC モード
  }
}
```

### 6.11 工数見積 (人日)

| ID | タイトル | 見積 | 主依存 |
|---|---|---|---|
| C0 | `MaxRev.Gdal.WindowsRuntime.Minimal` 実機 PoC (`gdalinfo --formats` 確認、Phase C 着手前提条件) | 0.5 | — |
| C1 | `windos-app.csproj` に GDAL パッケージ追加 + x64 固定 + `GdalBase.ConfigureAll()` `Program.Main` 先頭呼び出し + 配布サイズ確認 | 0.5 | C0 |
| C2 | `GdalLayerSource : ILayerSource` 骨格 (driver 名引数化、`ENCODING=CP932` Open オプション、Dispose で OGR DataSource 解放) | 1.5 | C1 |
| C3 | `ShapefilePackage` (zip → temp dir 展開、`IAsyncDisposable`、複数 SHP セット検出時はエラー) | 1.0 | C2 |
| C4 | `GdalInferenceStrategy` (OFT → InferredField 写像、サンプリング再推定) | 1.0 | C2 |
| C5 | OGR Geometry → GeoJSON 変換 (`Geometry.ExportToJson()` + MultiPolygon/MultiLineString 固定 + Z/M 値の skip) | 1.0 | C2 |
| C6 | `ISridDetector` 抽象 + `OgrSridDetector` / `ManualSridDetector` 実装 + 3 値設定駆動フォールバック | 1.0 | C2 |
| C7 | `ImportWizardForm` Step1 拡張 (SHP 活性化、inline 表示、ViewModel `SridResolutionState` / `DetectedEncoding` 公開) | 1.2 | C2, C3, C6 |
| C8 | `SridConverter.RegisterWkt` API 公開 + ユニットテスト (動的登録 → IsSupported → 変換) | 0.5 | — |
| C9 | `IEncodingResolver` インタフェース + `CpgFileParser` 純粋関数 + `[Theory]` テスト | 0.5 | — |
| C10 | `GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` + `ICollectionFixture<GdalFixture>` + `InternalsVisibleTo` 追加 | 1.0 | C2〜C6 |
| C11 | E2E: 10 万件合成 SHP 投入 + メモリ 2GB 以下確認 (`tests/fixtures/generate-large-shp.ps1` 経由) | 0.8 | C2〜C7 |
| C12 | ViewModel ヘッドレステスト (`SridResolutionState` 4 値、`DetectedEncoding` 反映) | 0.5 | C7 |
| C13 | `docs/layer-import.md` Phase C セクション + Phase C' 申し送り + WebGIS 非影響明記 + `PHASE_C_INDEX.md` | 0.5 | C2〜C11 |
| **合計** | | **11.5** | |

Plan 見積 9〜12 人日の上限近辺。案 A の 16.5 人日からは KML / MIF / TAB / モーダル / OgrDriverRegistry 排除で圧縮、案 B の Phase C 本体相当 (約 7〜8 人日) からは `RegisterWkt` API / `ISridDetector` / 10 万件 E2E / ViewModel テスト追加で増分。

### 6.12 Phase C' (続編) 申し送り

1. **MIF/MID 対応**: `GdalLayerSource` の driver 名引数を `"MapInfo File"` で渡すだけ。`MifPackage` (`.mif/.mid` ペア検証) を追加 (1.5 人日)
2. **TAB 対応**: 同上、`TabPackage` (`.tab/.dat/.id/.map/.ind` セット検証) を追加 (1.5 人日)
3. **`IImportPackage` 抽象切り出し**: Shapefile/MIF/TAB の 3 実装が揃った時点で共通化 (1.0 人日)
4. **和歌山旧測地系等ローカル CS の WKT 本体収録**: `appsettings.json: Import:SridCatalog[]` 経路で外部設定化 + 実機検証 (1.5 人日)
5. **`UcsDetectResolver` 実装**: UTF-16 / EUC-JP / CP949 自動検出が必要になった時点で `IEncodingResolver` の 2 実装目として追加 (0.8 人日)

### 6.13 Phase D 申し送り

1. **KML / KMZ 対応**: LIBKML が `Minimal` SKU で利用可能か再確認、ラスタ含む KMZ の扱い
2. **GeoPackage / FGB / GPX / DXF**: `IImportPackage` 抽象が確立済の前提で driver 名 + extension 登録の 1 行追加
3. **`BulkInsertMaxCount` 上限解除**: 10 万件 E2E 結果を見て `fn_feature_bulk_insert` 専用関数の必要性を判断 (audit_log バッチ集約は **採用しない方針を堅持**)
4. **サーバ側 GDAL** (GeoServer / 内製タイラ): `GdalBootstrap` / `GdalLayerSource` をサーバ側に移植する経路を再評価
5. **ClickOnce / MSIX 差分配布**: GDAL ネイティブ DLL 60〜80MB の差分配布最適化

### 6.14 設計論点 15 件への回答サマリ

| # | 論点 | 案 P の採択 |
|---|---|---|
| 1 | Shapefile zip 展開の責務 | (a) `ShapefilePackage` で実 temp dir 展開 (実装リスク Design 決定 3) |
| 2 | 文字コード判定 | (b) `.cpg` 優先 + 無ければ `Import:DefaultDbfEncoding` フォールバック + UI ComboBox 上書き |
| 3 | SRID 検出失敗時のフォールバック | **3 値設定駆動** (`Import:SridFallbackPolicy = Reject \| PromptUser \| AssumeWgs84`、デフォルト `PromptUser`) |
| 4 | OGR FieldDefn → InferredField マッピング | (a) OFT 完全準拠 + (b) 実値サンプリングで nullable 再推定 (ハイブリッド) |
| 5 | 大規模 Shapefile メモリ管理 | (a) `IAsyncEnumerable<GeoJsonFeature>` 逐次 yield (Phase B Chunker と整合) |
| 6 | GDAL ネイティブ DLL 配布 | (a) `WindowsRuntime.Minimal` 同梱、配布 zip 60〜80MB 直含 |
| 7 | Step1 UI 有効化方法 | (a) 「Phase C 対応予定」ラベル削除 + SHP のみ活性化 (MIF/TAB は Phase C' ラベルで非活性) |
| 8 | ジオメトリ型混在の正規化 | (c) feature 単位 `MultiPolygon/MultiLineString` 固定 (案 C 採用) |
| 9 | `BulkInsertMaxCount` 上限解除 | (a) Phase C スコープ外 (5000 据え置き、Phase D で再判断) |
| 10 | ローカル SRS サポート方針 | (c) `RegisterWkt` API のみ Phase C 公開、WKT 本体収録は Phase C' で `appsettings.json` 経路 |
| 11 | SHP と TAB の文字コード差異 | (b) Phase C は SHP のみ、TAB の MITAB 既定 + CharSet パースは Phase C' で `IEncodingResolver` 2 実装目に分離 |
| 12 | MITAB の 64bit/32bit 環境差 | (c) WinForms 全体を x64 固定 (`<PlatformTarget>x64</PlatformTarget>`) |
| 13 | 部分的に読めない feature のエラー | (a) skip + WARN ログ + Step3 完了ダイアログでスキップ件数表示 |
| 14 | OGR Geometry → GeoJSON 変換経路 | (a) OGR 標準 `Geometry.ExportToJson()` (Phase B System.Text.Json 直書き方針と整合) |
| 15 | `GdalBase.ConfigureAll()` 呼び出し位置 | (a) `Program.Main` 先頭 + `ICollectionFixture<GdalFixture>` でテスト並列耐性確保 |

## 7. ファイル別差分マップ (Issue 化の入力)

### WinForms (windos-app/)
- `windos-app.csproj` (C1): `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` 追加、`<PlatformTarget>x64</PlatformTarget>`、`InternalsVisibleTo("windos-app.tests")`
- `Program.cs` (C1): `Main` 先頭で `GdalBase.ConfigureAll()` 呼び出し (`appsettings.json: Gdal:ConfigureOnStartup` で切替可能)
- `Services/Import/GdalLayerSource.cs` (C2, 新規)
- `Services/Import/Packages/ShapefilePackage.cs` (C3, 新規)
- `Services/Import/InferenceStrategies/GdalInferenceStrategy.cs` (C4, 新規)
- `Services/Import/Encoding/IEncodingResolver.cs` + `CpgFileParser.cs` (C9, 新規)
- `Services/Import/Srid/ISridDetector.cs` + `OgrSridDetector.cs` + `ManualSridDetector.cs` (C6, 新規)
- `Services/Import/SridConverter.cs` (C8, 既存に `RegisterWkt(int, string)` 追加)
- `Forms/ImportWizardForm.cs` (C7, 既存に Shapefile 選択 + inline 表示)
- `ViewModels/ImportWizardViewModel.cs` (C7, `SridResolutionState` / `DetectedEncoding` / `DetectedSrid` プロパティ追加)
- `appsettings.json` (C6): `Import:SridFallbackPolicy` / `Import:DefaultDbfEncoding` / `Gdal:ConfigureOnStartup` セクション追加

### Tests (windos-app.tests/)
- `Tests/Services/Import/GdalLayerSourceTests.cs` (C10, `ILayerSourceContractTests<GdalLayerSource>` 継承)
- `Tests/Services/Import/GdalFixture.cs` (C10, `ICollectionFixture`、`GdalBase.ConfigureAll()` 1 回化)
- `Tests/Services/Import/GdalInferenceStrategyTests.cs` (C10, 純粋関数 [Theory])
- `Tests/Services/Import/CpgFileParserTests.cs` (C9, `[Theory]` 5〜8 ケース)
- `Tests/Services/Import/SridConverterRegisterWktTests.cs` (C8, 動的登録 API)
- `Tests/ViewModels/ImportWizardViewModelGdalTests.cs` (C12, `SridResolutionState` 4 値遷移)
- `Tests/E2E/ShapefileLargeImportTests.cs` (C11, 10 万件、`[Trait("Category","Performance")]`)
- `Fixtures/import/shapefile/points_4326_cp932.zip` + `polygons_6668_utf8.zip` + `no_prj.zip` (C10, 手作り最小)
- `tests/fixtures/generate-large-shp.ps1` (C11, 10 万件合成スクリプト)

### Docs
- `docs/layer-import.md` (C13, Phase C セクション + Phase C'/D 申し送り + WebGIS 非影響明記)
- `docs/PHASE_C_INDEX.md` (C13, Issue 一覧)

## 8. リスクと緩和

| リスク | 緩和策 |
|---|---|
| C0 PoC で `Minimal` SKU に SHP driver が含まれない | Phase C 着手延期、`MaxRev.Gdal.WindowsRuntime` (Full SKU) への切替を Issue 起票 |
| C11 で 10 万件投入が 2GB を超える | `IAsyncEnumerable` 逐次 yield + Chunker chunk サイズを 500 に下げる (Phase B `appsettings.json: ChunkDefaultSize` 変更のみ) |
| `appsettings.json: Import:SridFallbackPolicy=AssumeWgs84` を選んだ運用が事故を起こす | デフォルトを `PromptUser` 固定、AssumeWgs84 は `audit_log.meta_jsonb.srid_inferred=true` 必須化で事後追跡可能に |
| `ICollectionFixture<GdalFixture>` で並列実行が予想通り動かない (xUnit の Collection 仕様) | `[Collection("Gdal")]` 単純化にフォールバック、`windos-app.tests` の並列度を Phase C で計測し受容可能か判断 |
| `InternalsVisibleTo` 追加で production の API 表面が肥大化 | internal static の純粋関数のみ対象、public API は変更しない方針を Issue 説明欄に明記 |
| Phase C' で MIF/TAB を追加する時に `ShapefilePackage` 専用最適化が再合致しない | Phase C' Design で `IImportPackage` 抽象化を実施 (Phase C で先取りしないことで初期コスト削減 + Phase C' Design で改めて適切な抽象境界を確定) |

## 9. 次ステップ

- 本案 P を入力に Issue 化フェーズへ進む (`docs/issues/PHASE_C_ISSUES_INDEX.md` を新規作成)
- WBS C0〜C13 を 1 Issue 1 PR でラベル付け (`phase:C`, `area:winforms/tests/docs`, `phase-c-prime` で Phase C' 申し送り Issue を明示分離)
- **stacked PR pitfall** (MEMORY.md) に従い、すべての PR の `base=main` で固定
- C0 (`Minimal` SKU PoC) を最初の 1 Issue として立て、結果次第で C1 以降の着手可否を確定
- C11 (10 万件 E2E) の合成スクリプト結果を見て `BulkInsertMaxCount` 上限解除を Phase D で扱うか Phase C 内で前倒すか再判断
