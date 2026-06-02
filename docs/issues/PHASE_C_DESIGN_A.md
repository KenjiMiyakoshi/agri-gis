# agri-gis Phase C Design — 案 A「完全 GDAL ラッパ + KML 先取り」

Phase C Plan (`PHASE_C_PLAN.md`) で起こした 15 論点に対する **案 A** の回答。コンセプトは「**GDAL/OGR を全面的に基盤化し、Shapefile/MIF/TAB に加えて KML も先取り対応する**」。形式追加コストを最小化する基盤投資寄りの案で、Plan の S/M/L 工数より厚く出る一方、Phase D の Raster タイル化や追加ベクタ形式 (GeoPackage / FlatGeobuf) を後乗せしやすくする。

## 1. コンセプト要約 (ここで決めうちした 8 つ)

1. **対応形式は最初から 4 種**: Shapefile / MIF+MID / TAB / **KML** (将来要件先取り)。OGR ドライバを介すコストは形式数に対しほぼ線形でないため、初期投入時に揃える方が後段の WBS 圧縮に効く
2. **`GdalLayerSource` 1 本 + driver マッピング table**: Form 側に分岐は出さず `SourceFormat (= "shapefile"|"mif"|"tab"|"kml") → OGR Driver` を `Services/Import/Gdal/OgrDriverRegistry.cs` で集中管理
3. **入力パッケージは `ImportPackage` で抽象化**: 単一ファイル (KML)、複数ファイル組 (SHP の .shp/.shx/.dbf/.prj/.cpg、MIF の .mif/.mid、TAB の .tab/.dat/.id/.map/.ind)、zip 包の全パターンを 1 つの値オブジェクトに集約。寿命は `IAsyncDisposable` で temp dir まで責任を持つ
4. **文字コードは UCSDet + ENCODING オプション + UI 確認の三段**: 自動推測 → CP932/UTF-8/Shift_JIS/CP1252 の ComboBox プリセット → OGR の `ENCODING` Open オプションで渡す。プロセス環境変数 `SHAPE_ENCODING` は採用しない (Phase B 申し送り通り)
5. **SRID は多段フォールバック**: OGR `SpatialReference.AuthorityCode("EPSG")` → 既知 SRID キャッシュ (`SridConverter.IsSupported`) → WKT 直接登録 (`SridConverter.RegisterWkt`) → 失敗時 Step1 で手動 SRID 指定ダイアログ。黙って 4326 にはしない
6. **大規模ファイル対応**: `OGR Layer.GetNextFeature()` を `IAsyncEnumerable<GeoJsonFeature>` で 1 件ずつ yield。Chunker が 1000 件束ねるので WinForms 側にメモリ常駐させない。Feature は `using` で明示破棄
7. **`BulkInsertMaxCount` は据え置き 5000**: 案 A でも値変更はせず、10 万件投入の実測ログだけ Phase C で取り、Phase D で評価する (Phase B 申し送り通り)
8. **配布は x64 固定 + ネイティブ DLL 同梱**: `<PlatformTarget>x64</PlatformTarget>` を `windos-app.csproj` に明記、`MaxRev.Gdal.WindowsRuntime.Minimal` をビルド成果物に展開、起動時 `GdalBase.ConfigureAll()` を `Program.Main` の **最初の I/O 前** に呼ぶ

## 2. 新規/変更ファイル

### 新規 (WinForms 側)

| パス | 役割 |
|---|---|
| `windos-app/Services/Import/Gdal/GdalLayerSource.cs` | `ILayerSource` 実装本体。`ImportPackage` + driver 名 + `EncodingResolver` + `SridResolver` をコンストラクタ注入 |
| `windos-app/Services/Import/Gdal/OgrDriverRegistry.cs` | `SourceFormat → OGR ドライバ名 + 拡張子セット + 文字コード戦略` の table。SHP="ESRI Shapefile" / TAB="MapInfo File" / MIF="MapInfo File" / KML="LIBKML" (なければ "KML") |
| `windos-app/Services/Import/Gdal/ImportPackage.cs` | 入力ファイル群を表す値オブジェクト。`PrimaryPath` (OGR に渡すパス) / `SidecarPaths` / `TempRoot` / `IsTransient` / `DisposeAsync` |
| `windos-app/Services/Import/Gdal/ImportPackageBuilder.cs` | ユーザが選んだファイル/フォルダ/zip から `ImportPackage` を構築。SHP zip → temp 展開、TAB フォルダ → そのまま、MIF → .mid 同居確認 |
| `windos-app/Services/Import/Gdal/EncodingResolver.cs` | `.cpg` 参照 + UCSDet 推測 + ユーザ指定の合成。返り値は `ENCODING=CP932` 等の OGR Open オプション key/value |
| `windos-app/Services/Import/Gdal/SridResolver.cs` | OGR `SpatialReference` → EPSG コード or WKT → `SridConverter` に問い合わせ。失敗時は `SridResolution.Unknown(wkt)` を返し UI に判断を委譲 |
| `windos-app/Services/Import/Gdal/OgrGeometryToGeoJson.cs` | `OSGeo.OGR.Geometry.ExportToJson()` ベース。Z/M は drop、`Polygon` 単独は `Polygon` のまま (案 A は型維持) |
| `windos-app/Services/Import/InferenceStrategies/GdalInferenceStrategy.cs` | `OGR FieldDefn` → `InferredField` の純粋関数化マッピング |
| `windos-app/Services/Import/Gdal/GdalBootstrap.cs` | `GdalBase.ConfigureAll()` を 1 回だけ呼ぶ static gate。`Program.Main` から早期呼び出し |
| `windos-app/Forms/SourceConfirmDialog.cs` | Step1 直後に表示する「検出された文字コード / SRID / Feature 件数」確認モーダル |

### 変更 (WinForms 側)

| パス | 変更内容 |
|---|---|
| `windos-app.csproj` | `<PlatformTarget>x64</PlatformTarget>` 明記、`MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` 追加、`<RuntimeIdentifier>win-x64</RuntimeIdentifier>` 任意付与 |
| `Program.cs` | `Main` 先頭で `GdalBootstrap.EnsureConfigured()` を呼ぶ (`Application.SetHighDpiMode` の前) |
| `windos-app/Forms/ImportWizardForm.cs` | Step1 の「Phase C 対応予定」ラベル削除、SourceFormat ComboBox に shapefile/mif/tab/kml を追加、ファイル選択ダイアログを `SourceFormat` 切替対応に変更、`SourceConfirmDialog` 起動を Step1→Step2 遷移時に挿入 |
| `windos-app/ViewModels/ImportWizardViewModel.cs` | `OptionalEncoding (string?)` / `OptionalSrid (int?)` プロパティ追加 (INotifyPropertyChanged)、Step1 完了条件に `SridResolution.IsConfirmed` を追加 |
| `windos-app/Services/Import/SridConverter.cs` | `RegisterWkt(int srid, string wkt)` / `RegisterProj(int srid, string proj4)` を追加し、和歌山測地系等のローカル CS を WKT カタログから動的に流し込めるようにする |
| `windos-app/appsettings.json` | `GdalImport:DefaultEncoding=CP932` / `GdalImport:DefaultKmlDriver=LIBKML` / `Srs:LocalCatalogPath=./srs-catalog.json` を追加 |

### 新規 (Docs / 設定)

| パス | 役割 |
|---|---|
| `windos-app/srs-catalog.json` | ローカル SRS の WKT カタログ。和歌山測地系 (仮 SRID `900001` 以降の private 範囲) を 1 件以上、その他 EPSG 非登録系は需要に応じて追加 |
| `docs/srs-catalog.md` | private SRID 採番ルール (900001-999999) + WKT 採取手順 (QGIS Layer Properties → CRS Definition から WKT 1 をコピー) + レビュー手順 |
| `docs/layer-import.md` (追記) | Phase C 対応形式表 (SHP/MIF/TAB/KML)、文字コード自動推測の根拠、GDAL ネイティブ DLL の同梱サイズと初期化、未サポート CS のフォールバック手順 |

### 新規 (テスト)

| パス | 役割 |
|---|---|
| `windos-app.tests/Tests/Services/Import/Gdal/GdalLayerSourceTests.cs` | `ILayerSourceContractTests<GdalLayerSource>` 継承。SHP/MIF/TAB/KML 各 1 ファイルで契約テスト自動実行 |
| `windos-app.tests/Tests/Services/Import/Gdal/EncodingResolverTests.cs` | `.cpg` 優先 / UCSDet / ユーザ指定の優先順位検証 |
| `windos-app.tests/Tests/Services/Import/Gdal/SridResolverTests.cs` | EPSG 直取得 / WKT フォールバック / 不明系の `Unknown` 返却 |
| `windos-app.tests/Tests/Services/Import/Gdal/ImportPackageBuilderTests.cs` | zip 展開 / 複数ファイル組 / temp 後始末 |
| `windos-app.tests/Fixtures/import/shp/`, `mif/`, `tab/`, `kml/` | 最小サンプル一式 + 和歌山測地系 TAB (実機検証用) |

## 3. API 変更 — 原則なし

`POST /api/admin/layers/{id}/features/bulk` は Phase B 実装をそのまま使う。bulk の chunk size、`fn_feature_insert` ループ、`layer_import_job` の start/finalize 経路、`audit_log` の actor_user_id 等は **一切変更しない**。例外は 1 点だけ:

- `LayerCreate` リクエストの `source_format` enum に **`"shapefile"|"mif"|"tab"|"kml"`** を追加 (validation のみ。DB 列は `text` なので migration 不要)

## 4. WinForms UI フロー

### Step1: ファイル選択 + 自動検出

1. SourceFormat ComboBox: `geojson / csv / shapefile / mif / tab / kml` (Phase C で全活性化)
2. SourceFormat 選択後、ファイル選択ダイアログのフィルタを動的に変更:
   - shapefile: `*.zip;*.shp`
   - mif: `*.mif`
   - tab: `*.tab;*.zip` (zip は複数ファイル組同梱)
   - kml: `*.kml;*.kmz`
3. 選択後、裏で `ImportPackageBuilder` が package 構築 → `GdalLayerSource` 仮インスタンス化 → OGR で開かずに `SpatialReference` と `FieldDefn` だけ覗き見る
4. `SourceConfirmDialog` をモーダル表示:
   - 検出文字コード (UCSDet 結果 + 信頼度) → ユーザが上書き可能 (CP932 / UTF-8 / Shift_JIS / CP1252)
   - 検出 SRID (EPSG コード or "不明 (WKT)") → 不明時は手動 SRID 入力 (or "ローカル CS として WKT 登録" ボタン)
   - レイヤ件数 (Feature count) と幾何タイプ
5. 確認後、ViewModel の `OptionalEncoding` / `OptionalSrid` が確定 → Step2 へ

### Step2: スキーマ確認

既存 SchemaGrid をそのまま使う。`GdalInferenceStrategy.Infer(featureDefn)` 結果を流し込むだけ。

### Step3: 投入実行

既存の Chunker + Bulk 経路をそのまま使う。GDAL からは `IAsyncEnumerable<GeoJsonFeature>` で 1 件ずつ流れてくるので Chunker が 1000 件束ねる。

## 5. GDAL 依存の配布戦略

- **パッケージ**: `MaxRev.Gdal.Core` (managed) + `MaxRev.Gdal.WindowsRuntime.Minimal` (native, x64 only)
- **配布サイズ**: 展開後ネイティブ約 60〜80MB (gdal/proj/geos/sqlite 一式)。配布 zip は 100MB 弱に膨らむが、ClickOnce/MSIX の差分配布は Phase C スコープ外で **直 zip 配布**
- **初期化**: `GdalBootstrap.EnsureConfigured()` を `static bool _configured` + `lock` で idempotent に。`Program.Main` の `Application.SetHighDpiMode` より前で 1 回呼ぶ。テストプロジェクトは xUnit `CollectionFixture` で 1 回呼ぶ (並列実行時の static 汚染回避)
- **PROJ データ**: `MaxRev.Gdal.WindowsRuntime.Minimal` は `proj.db` 同梱なので追加配置不要。`PROJ_LIB` 環境変数は触らない
- **CI**: GitHub Actions の windows-latest で動作可。Linux/Mac は GDAL ロードできないので `[Trait("Platform","Windows")]` で skip

## 6. スキーマ推論 (`GdalInferenceStrategy`)

`OGR.FieldDefn` → `InferredField` の純粋関数マッピング。**Phase B CsvInferenceStrategy が実値サンプリングで再推定する流儀**は採らず、OFT 型を信頼する (案 A は OGR 完全信頼)。

| OFT 型 | InferredField.DataType | 備考 |
|---|---|---|
| OFTInteger | integer | width 無視 |
| OFTInteger64 | integer | C# long 相当 |
| OFTReal | number | width/precision 無視 |
| OFTString | string | width は max_length に転記 (validation のみ) |
| OFTDate | date | |
| OFTDateTime | datetime | TZ なし扱い |
| OFTTime | string | DB 側に time 型を切らない (Phase B 方針継続) |
| OFTBinary | (skip + WARN) | Phase C では未サポート、property から落とす |
| OFTStringList ほか List 系 | string (JSON 文字列化) | bbox/カテゴリ等の用途を救う |

`feature_current.properties` は `jsonb` なので型欠落は実害なし。

## 7. 15 論点への回答

### 論点 1 — Shapefile zip 展開の責務 → (a) `ImportPackageBuilder` で実 temp dir 展開

`/vsizip/` は OGR 単体では動くが、`.cpg` の auto-detect / `MITAB` の sidecar 検索 / Windows 上の長いパス問題があり、案 A は **temp dir 実展開**を採る。`ImportPackage.DisposeAsync` で temp 削除を保証。論点 11 の SHP/TAB 文字コード差にも対応しやすい。

### 論点 2 — 文字コード判定 → (a)+(c) UCSDet 自動推測 + UI 確認

`.cpg` があれば最優先、次に `.dbf` の先頭数 KB を UCSDet (Mozilla の文字コード判定移植 NuGet) にかけ CP932/UTF-8/Shift_JIS/CP1252 の信頼度を出す。`SourceConfirmDialog` で ComboBox プリセット表示、ユーザが上書き可能。確定値を OGR `ENCODING` Open オプションで渡す。MIF はヘッダの `CharSet` 句を別パスでパース (`EncodingResolver.ReadMifHeader`)。

### 論点 3 — SRID 検出失敗時のフォールバック → (b) ユーザ手動指定を要求

黙って 4326 は **採用しない**。Step1 の `SourceConfirmDialog` で SRID 未解決時は ComboBox (EPSG 既知一覧) + 手動入力 + 「ローカル CS として WKT 登録」ボタンを出し、`OptionalSrid` が `null` のままなら Step2 へ進ませない。Phase B `CsvLayerSource` がユーザ指定 SRID を取る流れと整合。

### 論点 4 — OGR FieldDefn → InferredField マッピング → (a) OFT 型完全準拠

§6 表のとおり OFT 完全準拠。CsvInferenceStrategy の boolean/date 再推定方式は SHP の `.dbf` 型情報が信頼できるため不要。`feature_current.properties` が jsonb なので推論ミスの実害も低い。

### 論点 5 — 大規模ファイルのメモリ管理 → (a) `IAsyncEnumerable` で逐次 yield + Feature 明示破棄

`OGR.Layer.GetNextFeature()` を `while` で回し、`using var feature = ...` のスコープで 1 件ずつ破棄しながら `yield return`。Chunker が 1000 件で束ねるので WinForms 側にメモリ常駐させない。SQL `LIMIT/OFFSET` は OGR ドライバ間で挙動差が大きく採らない。

### 論点 6 — GDAL ネイティブ DLL の配布 → (a) `WindowsRuntime.Minimal` を bin に同梱

直 zip 配布。ClickOnce/MSIX の差分配布は Phase D 以降の検討事項。`Minimal` を選び GDB/HDF/NetCDF 等を除外することで 60〜80MB に収まる。

### 論点 7 — Step1 UI 解禁方法 → (a) ラベル削除 + 全項目活性化

Feature Flag は採らない。Phase B で書いてあった「Phase C 対応予定」ラベルを削除し、`SourceFormat` ComboBox の `shapefile/mif/tab/kml` を活性化。リリース単位は Phase C 全体で 1 つ。

### 論点 8 — ジオメトリ型混在の正規化 → (c) Polygon/MultiPolygon は維持

`feature_current.geom` が `geometry(Geometry, 3857)` で型不問なので、案 A は **OGR が返す型をそのまま `geometry` JSON にする**。Polygon と MultiPolygon が同レイヤに混在しても DB は受け入れる。`layers.geometry_type` には OGR `Layer.GetGeomType()` の値を素直に転記 (混在検出時は `GeometryCollection`)。

### 論点 9 — `BulkInsertMaxCount` 上限解除 → (a) スコープ外、据え置き 5000

案 A でも Phase B 申し送り通り据え置き。Phase C 中に 10 万件投入のログ (chunk 数 / Tx 平均時間 / actor_user_id × audit 件数) だけ取り、Phase D で評価する。

### 論点 10 — ローカル SRS サポート方針 → (a)+(c) WKT 直書きカタログ + 動的登録 API

`SridConverter.RegisterWkt(int srid, string wkt)` と `RegisterProj(int srid, string proj4)` を新設。`srs-catalog.json` から起動時に一括登録 (`Program.Main`)。private SRID は `900001` から採番、`docs/srs-catalog.md` で採番ルールと WKT 採取手順を文書化。和歌山測地系は EPSG コードを持たないので `900001` を割り当てて 1 件目を入れる。

### 論点 11 — SHP と TAB の文字コード差異 → (b) 形式別にデフォルトを分け、UI で上書き可能

SHP デフォルト `ENCODING=CP932`、TAB デフォルトは MITAB ドライバ既定 (`CharSet` 句尊重)、MIF は同じく `CharSet` 句尊重。`OgrDriverRegistry` に「デフォルト ENCODING を渡すか否か」のフラグを持たせ、`EncodingResolver` がドライバ別に異なる動作をする。KML は UTF-8 固定。

### 論点 12 — 64bit/32bit 環境差 → (c) WinForms アプリ全体を x64 固定

`<PlatformTarget>x64</PlatformTarget>` を `windos-app.csproj` に明記。AnyCPU + 実行時ロードは `MaxRev.Gdal.WindowsRuntime.Minimal` が x64 のみ提供のため不可能。ARM64 Windows は対象外。

### 論点 13 — 部分的に読めない feature のエラーハンドリング → (c) skip + 上限超過で fail-fast

デフォルトは skip + WARN ログ + Step3 完了ダイアログにスキップ件数表示。`appsettings: GdalImport:MaxSkipRatio=0.01` を超えたら `ImportException` で fail-fast、bulk Tx をロールバック。`layer_import_job` の `error_count` に集約。

### 論点 14 — OGR Geometry → GeoJSON 変換経路 → (a) `Geometry.ExportToJson()` ベース

NTS は導入しない (Phase B が System.Text.Json 直書きで揃えている方針と整合)。`ExportToJson()` の戻り文字列を `JsonDocument.Parse` → `RootElement.Clone()` で `GeoJsonFeature.Geometry` に流し込む (`GeoJsonLayerSource.cs` の既存パターンに揃う)。Z/M は drop (`Geometry.SetCoordinateDimension(2)` で 2D 化してから export)。

### 論点 15 — `GdalBase.ConfigureAll()` 呼び出し位置 → (a) `Program.Main` 先頭

`GdalBootstrap.EnsureConfigured()` を `static bool _configured` + `lock` で idempotent に。Production は `Program.Main` の最初の I/O 前で 1 回、テストは xUnit `CollectionFixture` で 1 回。起動コスト (~100ms) は気にしない。Lazy は static 状態汚染が xUnit 並列実行と相性悪い (Phase B `SHAPE_ENCODING` 不採用と同じ理由)。

## 8. 工数見積 (人日、案 A)

| WBS | 区分 | 案 A での粒度 | 人日 |
|---|---|---|---|
| C1 | WinForms | csproj 編集 + x64 固定 + `GdalBootstrap` + 配布サイズ計測 + README | 1.0 |
| C2 | WinForms | `GdalLayerSource` + `OgrDriverRegistry` + `ImportPackage` + Lifetime 管理 (4 形式分) | 3.0 |
| C2-K | WinForms | KML 先取り (LIBKML driver 経路 + フォルダ階層 → properties 変換) | 1.0 |
| C3 | WinForms | `ImportPackageBuilder` (zip 展開 / TAB sidecar 検索 / MIF+MID ペア検証 / KMZ 展開) | 1.5 |
| C4 | WinForms | `GdalInferenceStrategy` (OFT 全種マッピング + List 系 JSON 化) | 1.0 |
| C5 | WinForms | `OgrGeometryToGeoJson` (`ExportToJson()` ラッパ + Z/M drop + 例外正規化) | 0.5 |
| C6 | WinForms | `SridResolver` + `SridConverter.RegisterWkt`/`RegisterProj` + srs-catalog ローダ | 1.5 |
| C7 | WinForms | `ImportWizardForm` Step1 拡張 + `SourceConfirmDialog` + ViewModel 拡張 | 2.0 |
| C8 | WinForms | 和歌山測地系 WKT 採取 + `srs-catalog.json` 整備 + private SRID 採番ルール文書 | 0.8 |
| C9 | Test | `GdalLayerSourceTests` (4 形式) + `EncodingResolverTests` + `SridResolverTests` + `ImportPackageBuilderTests` | 2.0 |
| C10 | Test | 和歌山測地系 TAB E2E + 10 万件 SHP メモリ実測 + skip ratio 境界 | 1.5 |
| C11 | Docs | `docs/layer-import.md` + `docs/srs-catalog.md` + `PHASE_C_INDEX.md` | 0.7 |
| **合計** | | | **約 16.5 人日** |

Plan の「9〜12 人日」より上振れの主因: **KML 先取り (+1.0)、`ImportPackage`/`OgrDriverRegistry`/`EncodingResolver`/`SridResolver` の基盤化 (+2.5)、`SourceConfirmDialog` UI (+1.0)、E2E 実機検証の厚み (+0.5)**。トレードオフは Phase D で形式追加 (GeoPackage / FlatGeobuf) する際の WBS が `OgrDriverRegistry` への 1 行追加 + テストフィクスチャ追加で済むこと。

## 9. Phase D 申し送り

- **追加ベクタ形式**: GeoPackage / FlatGeobuf は `OgrDriverRegistry` に driver 名追加 + サンプル fixture 追加で乗る (見積 2-3 人日/形式)
- **`BulkInsertMaxCount` 評価**: Phase C で取った 10 万件投入ログを基に 5000 → 20000-50000 の引き上げ判断
- **サーバ側 GDAL**: ラスタタイル化 / WMS 出力 が Phase D 候補。WinForms 同梱の GDAL 知見を API/Docker (Linux) に転写する手順を別途検討 (PROJ_LIB / proj.db 配置が WinForms と異なる)
- **選択ハイライトのサーバ raster**: Phase C のインポート経路と独立。`feature_current` の jsonb properties に依存しないので影響なし
- **MITAB の v3+ 系**: `MaxRev.Gdal.WindowsRuntime.Minimal` の MITAB は最新版未対応の可能性。Phase D で `Full` 版への置換を評価
- **KMZ 内画像**: Phase C では KMZ の画像 (Ground Overlay) は無視し vector のみ。Phase D のラスタ系で再検討
- **ClickOnce/MSIX 差分配布**: ネイティブ 60-80MB を毎回配るのは Phase D で改善対象

## 10. ここで決めうちした判断のまとめ (再確認用)

| 判断 | 値 | 理由 |
|---|---|---|
| 対応形式 | SHP/MIF/TAB + **KML 先取り** | 形式追加の限界費用を下げる基盤投資 |
| zip 展開 | temp dir 実展開 | `/vsizip/` の sidecar 検索不安定性回避 |
| 文字コード | UCSDet + UI 確認 + OGR `ENCODING` | 環境変数不採用 (xUnit 並列対応) |
| SRID 失敗時 | ユーザ手動指定を要求 | 黙って 4326 はサイレント事故源 |
| FieldDefn 写像 | OFT 完全準拠 | SHP/TAB の `.dbf` 型情報は信頼できる |
| メモリ管理 | `IAsyncEnumerable` 逐次 yield | Phase B Chunker と直接接続 |
| 配布 | 直 zip + x64 固定 + 同梱 | ClickOnce/MSIX は Phase D |
| ジオメトリ型 | OGR の型をそのまま転記 | DB が `geometry(Geometry)` で型不問 |
| `BulkInsertMaxCount` | 据え置き 5000 | Phase B 申し送り通り、Phase C で実測のみ |
| ローカル SRS | WKT カタログ + 動的登録 API | 和歌山系は EPSG 未登録のため |
| SHP/TAB 文字コード差 | ドライバ別デフォルト + UI 上書き | MITAB の `CharSet` 句を尊重 |
| 32/64bit | x64 固定 | ネイティブ DLL が x64 only |
| feature 読み取りエラー | skip + 上限超過で fail-fast | skip ratio を appsettings 制御 |
| OGR Geom → GeoJSON | `ExportToJson()` | Phase B の System.Text.Json 方針と整合 |
| `GdalBase.ConfigureAll()` | `Program.Main` 先頭 + idempotent gate | Lazy は static 汚染リスク |
