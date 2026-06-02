# agri-gis Phase C イシュー一覧 (案 P)

`PHASE_C_DESIGN_P.md` 分割。`C1xx`=WinForms 配線 / `C3xx`=スキーマ推論・OGR 写像 / `C4xx`=ImportWizardForm UI / `C5xx`=Tests / `C6xx`=Docs。API/DB 拡張は本 Phase ではゼロ (Phase B `fn_feature_insert` / `audit_log.meta_jsonb` をそのまま使用) のため C2xx は欠番。S=0.5d / M=1d / L=1.5d。

## 一覧 (15 Issue / 11.5d)

| # | タイトル | 工数 | 主担当 | 依存 |
|---|---|---|---|---|
| C100 | `Minimal` SKU 実機 PoC (C0、Phase C 着手前提条件) | S(0.5d) | winforms | — |
| C101 | GDAL NuGet + x64 固定 + `GdalBase.ConfigureAll()` 配線 | S(0.5d) | winforms | C100 |
| C102 | `GdalLayerSource : ILayerSource` 骨格 | L(1.5d) | winforms | C101 |
| C103 | `ShapefilePackage` (zip → temp dir 実展開) | M(1d) | winforms | C102 |
| C104 | `ISridDetector` 抽象 + 3 値設定駆動フォールバック | M(1d) | winforms | C102 |
| C105 | `IEncodingResolver` + `CpgFileParser` 純粋関数 | S(0.5d) | winforms | — |
| C106 | `SridConverter.RegisterWkt` API 公開 | S(0.5d) | winforms | — |
| C301 | `GdalInferenceStrategy` (OFT → InferredField 写像) | M(1d) | winforms | C102 |
| C302 | OGR Geometry → GeoJSON 変換 + Multi 正規化 | M(1d) | winforms | C102 |
| C401 | `ImportWizardForm` Step1 拡張 + inline 検出表示 | L(1.2d) | winforms | C102,C103,C104 |
| C501 | `GdalLayerSourceTests` + `ICollectionFixture<GdalFixture>` | M(1d) | tests | C102-C104,C302 |
| C502 | 純粋関数 [Theory] (RegisterWkt / CpgFileParser / Inference) | S(0.5d) | tests | C105,C106,C301 |
| C503 | E2E 10 万件合成 SHP 投入 + メモリ 2GB 受け入れ | S(0.8d) | tests | C401 |
| C504 | ViewModel ヘッドレス (`SridResolutionState` 4 値) | S(0.5d) | tests | C401 |
| C601 | `docs/layer-import.md` Phase C セクション + `PHASE_C_INDEX.md` | S(0.5d) | docs | C501-C503 |

クリティカルパス: C100 → C101 → C102 → C103/C104 並列 → C301/C302 並列 → C401 → C501/C503 → C601 (≈8〜9 営業日 + バッファ)。

ラベル: `phase:C` / `area:winforms|tests|docs` / `phase-c-prelude`(C100) / `phase-c-prime-followup`(MIF/TAB/WKT 本体収録の申し送り Issue) / `stretch`(C503)。

全 PR `base=main` 固定 (MEMORY.md `stacked_pr_pitfall`)。推奨着手順: C100 → C101 → C102 → (C103/C104/C105/C106/C301/C302 並列) → C401 → (C501/C502/C503/C504 並列) → C601。

---

## Issue 詳細

### C100 `MaxRev.Gdal.WindowsRuntime.Minimal` 実機 PoC (winforms/S/前提条件)

`MaxRev.Gdal.WindowsRuntime.Minimal` SKU に Shapefile driver (`ESRI Shapefile`) が含まれているかを実機で確認する。`gdalinfo --formats` または C# 1 ファイル PoC (`Gdal.AllRegister(); foreach (Driver d in ...)`) で `ESRI Shapefile` 出力を確認するだけ。Phase C 全体の着手前提条件 (実装リスクレビュー Design 決定 1)。

受け入れ条件:
- `tools/poc/GdalSkuCheck/` に最小 PoC プロジェクト or `gdalinfo --formats` 出力を `docs/issues/PHASE_C_C100_POC_RESULT.md` に記録
- `ESRI Shapefile` driver の含有を確認 (含まれない場合は `MaxRev.Gdal.WindowsRuntime` フル SKU 切替 Issue を新規起票)
- 配布サイズ (`%USERPROFILE%\.nuget\packages\maxrev.gdal.windowsruntime.minimal\` 配下) を記録
- C101 着手判断 (`go` / `no-go`) を PR description に明記

依存: なし
工数: S (0.5d)

### C101 GDAL NuGet + x64 固定 + `GdalBase.ConfigureAll()` 配線 (winforms/S)

`windos-app.csproj` に `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` を追加し、`<PlatformTarget>x64</PlatformTarget>` を固定。`Program.Main` 先頭で `GdalBase.ConfigureAll()` を 1 回呼び出す (`appsettings.json: Gdal:ConfigureOnStartup=true` で切替可能)。`InternalsVisibleTo("windos-app.tests")` を csproj に追加。

受け入れ条件:
- `dotnet build -c Release` 成功、配布 zip サイズが C100 PoC 値 ±10% 以内
- `Program.Main` 起動時に GDAL バナーログ (1 行) が出力されること
- `Gdal:ConfigureOnStartup=false` で起動した場合は `ConfigureAll()` を呼ばないこと
- `<InternalsVisibleTo Include="windos-app.tests" />` が csproj に存在
- `windos-app.tests` も x64 ターゲットで build 通過

依存: C100
工数: S (0.5d)

### C102 `GdalLayerSource : ILayerSource` 骨格 (winforms/L)

`Services/Import/GdalLayerSource.cs` を新規作成。`ILayerSource` (Phase B 確立) を実装し、`sourceFormat` を ctor 引数で受け取る (Phase C は `"shapefile"` のみ、Phase C' で `"mif"` / `"tab"` を同クラスで受ける)。`ShapefilePackage` / `ISridDetector` / `IEncodingResolver` を ctor 注入。`DisposeAsync` で OGR DataSource + temp dir を連鎖解放。`InferSchemaAsync` / `ReadFeaturesAsync` は中身を呼び出す形だけ用意 (実体は C103/C301/C302 で埋める)。

受け入れ条件:
- `sealed class GdalLayerSource : ILayerSource` を定義、`SourceFormat` / `SourceSrid` プロパティ公開
- ctor が `(ShapefilePackage, ISridDetector, IEncodingResolver, string sourceFormat = "shapefile")` を受け取る
- `Ogr.Open(path, new[] { $"ENCODING={resolved}" })` 形式の Open オプション経路 (環境変数 `SHAPE_ENCODING` 不使用)
- `DisposeAsync()` で OGR DataSource 解放 + `_package.DisposeAsync()` の二重連鎖
- ビルド成功、`GdalLayerSourceTests` 骨格 (C501) がコンパイル可能

依存: C101
工数: L (1.5d)

### C103 `ShapefilePackage` (zip → temp dir 実展開) (winforms/M)

`Services/Import/Packages/ShapefilePackage.cs` を新規作成。zip ファイルを `Path.GetTempPath()` 配下の一意ディレクトリに実展開し、`.shp / .shx / .dbf / .prj / .cpg` の存在を検証。`IAsyncDisposable` で temp dir を再帰削除。`/vsizip/` 仮想 FS は採用しない (実装リスク Design 決定 3)。複数 SHP セット同梱時は明示エラー。

受け入れ条件:
- `OpenAsync(string zipPath, CancellationToken ct) -> ValueTask<ShapefilePackage>` 静的ファクトリ
- temp dir 名は `gis-shp-{Guid}` で衝突回避、`DisposeAsync` で再帰削除
- `.shp` 必須、`.shx`/`.dbf`/`.prj`/`.cpg` 任意 (不在は警告ログのみ)、ZIP 内に `.shp` が 2 つ以上で `InvalidDataException`
- `ShpPath` / `PrjPath` / `CpgPath` プロパティ公開 (相対パスではなく絶対パス)
- C501 で `points_4326_cp932.zip` / `no_prj.zip` / `multi_shp.zip` の各ケースを assertion

依存: C102
工数: M (1d)

### C104 `ISridDetector` + 3 値設定駆動フォールバック (winforms/M)

`Services/Import/Srid/ISridDetector.cs` を新規作成。`OgrSridDetector` (`.prj` → `SpatialReference.AuthorityCode` 経路) と `ManualSridDetector` (UI 入力経路) の 2 実装を提供。`appsettings.json: Import:SridFallbackPolicy = Reject | PromptUser | AssumeWgs84` (デフォルト `PromptUser`) を読み、検出失敗時の挙動を 3 値で切り替え。`AssumeWgs84` 選択時は `srid_inferred=true` を `audit_log.meta_jsonb` に書く経路を Phase B `fn_feature_insert` の `p_meta_json` 引数で確保。

受け入れ条件:
- `ISridDetector.DetectAsync(ShapefilePackage, CancellationToken) -> ValueTask<SridDetectionResult>` シグネチャ
- `SridDetectionResult` は `int? Srid` + `SridResolutionState State` の 2 値 (`Detected | FallbackToPrompt | FallbackToWgs84 | Rejected`)
- `IOptions<ImportOptions>` で `SridFallbackPolicy` を注入、未指定時は `PromptUser`
- Fake で `.prj` 不在を再現したテスト (C501) で `FallbackToPrompt` / `Rejected` / `FallbackToWgs84` の遷移を assertion
- `AssumeWgs84` 経路で `meta_json` に `{"srid_inferred":true,"srid_fallback_policy":"AssumeWgs84"}` を組み立てるヘルパ公開

依存: C102
工数: M (1d)

### C105 `IEncodingResolver` + `CpgFileParser` 純粋関数 (winforms/S)

`Services/Import/Encoding/IEncodingResolver.cs` インタフェース + `CpgFileParser` 静的純粋関数を実装。`Parse(string raw) -> string?` で `.cpg` 内容 (`"CP932"`/`"932"`/`"UTF-8"`/`""` 等) を正規化。Phase C は `CpgFileResolver` 1 実装のみ。`UcsDetectResolver` は Phase D 申し送り。

受け入れ条件:
- `IEncodingResolver.Resolve(ShapefilePackage) -> string` (`Import:DefaultDbfEncoding` に fallback)
- `CpgFileParser.Parse(string)` は `"CP932"`/`"932"`/`"cp932"` → `"CP932"`、`""` / null → `null`、`"UTF-8"` → `"UTF-8"`
- UI ComboBox 上書き経路 (ViewModel) と分離 (Resolver は読み取り専用)
- C502 で `[Theory]` 7 ケース以上 (CP932 / 932 / UTF-8 / EUC-JP / 空文字 / 不正 / 改行末尾) 網羅

依存: なし
工数: S (0.5d)

### C106 `SridConverter.RegisterWkt` API 公開 (winforms/S)

Phase B `SridConverter` (Phase B B404) に `public void RegisterWkt(int srid, string wkt)` を追加。動的登録経路を Phase C 本体で確保し、`IsSupported(srid)` / `GetTransformation(from, to)` が動的登録 SRID に対しても動作することを保証。WKT 本体収録 (和歌山旧測地系等) は **Phase C' 送り** (`appsettings.json: Import:SridCatalog[]` 経路で外部設定化)。Phase C の責務は API のみ公開 + ユニットテスト担保。

受け入れ条件:
- `public void RegisterWkt(int srid, string wkt)` 公開、内部 dictionary に追加
- 重複登録は後勝ち (`Dictionary[key]=value` セマンティクス) で例外を投げない
- 不正 WKT は `ProjNet.IO.CoordinateSystems.CoordinateSystemWktReader` 例外を呼び出し側に伝播
- C502 で動的登録 → `IsSupported(99999)` true → 恒等変換 (`from==to`) 例外なしを assertion
- `appsettings.json: Import:SridCatalog[]` 読み込みは **Phase C' 送り** を実装コメントに明記

依存: なし
工数: S (0.5d)

### C301 `GdalInferenceStrategy` (OFT → InferredField 写像) (winforms/M)

`Services/Import/InferenceStrategies/GdalInferenceStrategy.cs` を新規作成。`IInferenceStrategy` (Phase B B403) を実装し、`SourceFormat => "shapefile"`。OGR `FieldDefn` を `InferredField` に写像する純粋関数。OFT 型表は Design 案 P §6.7 に従う。100 feature サンプリングで nullable / date 再推定。

受け入れ条件:
- `InferAsync(Stream, CancellationToken) -> Task<IReadOnlyList<InferredField>>` 実装 (Stream は `ShapefilePackage` ラッパ経由)
- OFT 型表: `OFTInteger/OFTInteger64 → integer`、`OFTReal → number`、`OFTString → string`、`OFTDate/OFTDateTime → date`、`OFTBinary → skip + WARN`、`OFTStringList/OFTIntegerList → string` (JSON 配列文字列化)
- サンプリング 100 feature で 1 件でも null / 空文字があれば `nullable=true`
- `OFTString` で 100 件全てが ISO8601 (`yyyy-MM-dd`) 形式なら `date` に格下げ昇格
- C502 で OFT 全 8 種 + ISO8601 昇格 + nullable 再推定の `[Theory]` 10 ケース以上

依存: C102
工数: M (1d)

### C302 OGR Geometry → GeoJSON 変換 + Multi 正規化 (winforms/M)

OGR `Geometry.ExportToJson()` を経由する GeoJSON 変換経路を `GdalLayerSource.ReadFeaturesAsync` に実装。feature 単位で `MultiPolygon` / `MultiLineString` に固定 (案 C 採用、Design 案 P §6.5 論点 8)。Z/M 値は skip (警告ログ)。`IAsyncEnumerable<GeoJsonFeature>` で逐次 yield (Phase B Chunker と整合)。

受け入れ条件:
- `Polygon` 単体 feature を `MultiPolygon` 1 リング、`LineString` 単体を `MultiLineString` 1 ライン に昇格
- `Point` / `MultiPoint` はそのまま (`feature_current.geom geometry(Geometry, 3857)` 制約と整合)
- Z/M 値を含む geometry は X/Y のみ抽出 + 警告ログ ("Z/M values dropped at feature {fid}")
- 部分的に読めない feature (`GetNextFeature()` 例外) は skip + WARN ログ + スキップ件数を `ImportWizardForm` Step3 完了ダイアログで表示 (案 P §6.14 論点 13)
- `IAsyncEnumerable<GeoJsonFeature>` で 10 万件 yield しても LOH に乗らない (C503 で計測)

依存: C102
工数: M (1d)

### C401 `ImportWizardForm` Step1 拡張 + inline 検出表示 (winforms/L)

Phase B `ImportWizardForm` (B408) Step1 の ComboBox を拡張し、`Shapefile ZIP` 項目を活性化。「Phase C 対応予定」ラベルを削除。MIF/TAB は「Phase C' 対応予定」ラベル + 非活性で残置。Shapefile 選択時に inline 表示 (モーダル `SourceConfirmDialog` は不採用、実装リスク Design 決定 5)。ViewModel に `DetectedEncoding` / `DetectedSrid` / `SridResolutionState` / `FieldCount` / `FeatureCount` を公開。`SridResolutionState ∈ { Detected, FallbackToPrompt, FallbackToWgs84, Rejected }` の 4 値。

受け入れ条件:
- ComboBox 項目: `GeoJSON` / `CSV` / `Shapefile ZIP` (活性) / `MapInfo MIF/MID (Phase C' 対応予定)` (非活性) / `MapInfo TAB (Phase C' 対応予定)` (非活性)
- ファイル選択ボタン → zip パス → [自動検出] ボタン → `ShapefilePackage.OpenAsync` → OGR Open 1 回 → ViewModel 4 プロパティ更新
- `SridResolutionState=Rejected` で `Next` ボタン非活性、`FallbackToPrompt` で手動 SRID 入力欄が出現、`Detected`/`FallbackToWgs84` で `Next` 活性
- 検出文字コード ComboBox で `CP932`/`UTF-8`/`EUC-JP` 上書き可能、上書き値で OGR Open し直し
- Step2 (SchemaGrid) / Step3 (投入) は Phase B B408 から無変更で動作

依存: C102, C103, C104
工数: L (1.2d、Design 工数 1.2 を L として表記)

### C501 `GdalLayerSourceTests` + `ICollectionFixture<GdalFixture>` (tests/M)

`Tests/Services/Import/GdalLayerSourceTests.cs` を `ILayerSourceContractTests<GdalLayerSource>` (Phase B B503) 継承で実装。`GdalFixture` を `ICollectionFixture<GdalFixture>` で 1 回化、`GdalBase.ConfigureAll()` を fixture 内で初回 1 回呼び (テスタビリティ補強 C-2)。`InternalsVisibleTo` 済 (C101) を前提に `BuildShapefilePath` 等の internal static 純粋関数も assertion 対象。Fixture: `points_4326_cp932.zip` / `polygons_6668_utf8.zip` / `no_prj.zip` (手作り最小)。

受け入れ条件:
- `[Collection("Gdal")]` + `ICollectionFixture<GdalFixture>` で並列実行効率を維持 ([Collection 単純化は不採用)
- `points_4326_cp932.zip` で `SourceFormat="shapefile"` / `SourceSrid=4326` / feature 数一致を assertion
- `no_prj.zip` で `SridResolutionState=FallbackToPrompt` (デフォルト `PromptUser`)、`SridFallbackPolicy=AssumeWgs84` 注入時に `FallbackToWgs84` を assertion
- `polygons_6668_utf8.zip` で `.cpg` から `UTF-8` 検出、文字化けせずに dbf 属性が読めること
- 契約テスト (非空 / 順次返却 / Dispose 後例外) は B503 ContractTests を継承するだけで通過

依存: C102, C103, C104, C302
工数: M (1d)

### C502 純粋関数 [Theory] (RegisterWkt / CpgFileParser / Inference) (tests/S)

C106 (`SridConverter.RegisterWkt`) / C105 (`CpgFileParser.Parse`) / C301 (`GdalInferenceStrategy.InferAsync`) の純粋関数を `[Theory]` で網羅。OGR モック不要 (純粋関数のため fixture 不要)。

受け入れ条件:
- `SridConverterRegisterWktTests`: 動的登録 → `IsSupported(99999)` true → 恒等変換、重複登録後勝ち、不正 WKT 例外伝播 (3 ケース以上)
- `CpgFileParserTests`: `[Theory]` 7 ケース以上 (CP932 / 932 / cp932 / UTF-8 / EUC-JP / 空文字 / 改行末尾)
- `GdalInferenceStrategyTests`: OFT 全 8 種 (Integer/Integer64/Real/String/Date/DateTime/Binary/StringList) + ISO8601 昇格 + nullable 再推定で 10 ケース以上
- 全テスト 2 秒以内、CI Linux ランナーでも実行可能 (OGR 依存を持つテストのみ x64 Windows runner にスキップ条件)

依存: C105, C106, C301
工数: S (0.5d)

### C503 E2E 10 万件合成 SHP 投入 + メモリ 2GB 受け入れ (tests/S/`stretch`)

`tests/fixtures/generate-large-shp.ps1` で起動時に 10 万件合成 SHP を temp に生成 → import 完走 → メモリ 2GB 以下を assertion → temp 削除。`[Trait("Category","Performance")]` で CI からは除外、ローカル / 夜間ジョブで実行。拡張性補強 5 採用 (100 万 → 10 万に下方修正)。

受け入れ条件:
- `generate-large-shp.ps1` が `ogr2ogr` または `Shapefile.NET` で 10 万件 Point feature を CP932 dbf + 4326 prj 付きで生成
- E2E が `ShapefileLargeImportTests.Import_100k_Features_Under_2GB_Memory()` 1 ケースで完走
- 最大 working set を `Process.GetCurrentProcess().PeakWorkingSet64` で計測、2GB 超過で Fail
- 投入時間を Console 出力、結果を C601 `layer-import.md` に「性能特性」として転記
- git に fixture をコミットしない (`.gitignore` に `tests/fixtures/large-shp/` 追加)

依存: C401
工数: S (0.8d、stretch ラベル)

### C504 ViewModel ヘッドレス (`SridResolutionState` 4 値) (tests/S)

`Tests/ViewModels/ImportWizardViewModelGdalTests.cs` を Phase B B505 と同パターンで実装。`Mock<IApiClient>` + `Mock<ISridDetector>` + `Mock<IEncodingResolver>` で UI スレッド非依存テスト (`Application.Run` 不要)。`SridResolutionState` 4 値遷移を assertion。

受け入れ条件:
- `.prj` 不在 SHP → `SridResolutionState=FallbackToPrompt` → 手動 SRID 入力 → `Detected` 遷移を assertion
- `SridFallbackPolicy=Reject` 注入で `Rejected` → `CanGoNext=false` を assertion
- `SridFallbackPolicy=AssumeWgs84` 注入で `FallbackToWgs84` → `meta_json` に `srid_inferred=true` 付与を assertion
- `DetectedEncoding` が `CpgFileParser` 結果と一致、UI ComboBox 上書きで再 Open される経路を Mock 検証
- B505 と同 fixture 構成、Linux ランナー OK (OGR 依存テストは別 Issue C501 に分離済)

依存: C401
工数: S (0.5d)

### C601 `docs/layer-import.md` Phase C セクション + `PHASE_C_INDEX.md` (docs/S)

Phase B B601 `docs/layer-import.md` に Phase C セクションを追記。Shapefile 対応形式表、`SridFallbackPolicy` 3 値運用ガイド、`.cpg` 優先 + ComboBox 上書き仕様、x64 固定の理由、C503 計測結果 (投入時間 + 最大 working set)、Phase C' / Phase D 申し送り (案 P §6.12/§6.13 転記)、WebGIS 非影響明記 (案 P §5.1 補強 6)。`docs/PHASE_C_INDEX.md` を新規作成し、Issue 一覧 + シナリオ + リンクを 1 枚にまとめる。

受け入れ条件:
- `docs/layer-import.md` に「Phase C 対応形式」「Phase C 設定 (`Import:SridFallbackPolicy` 3 値 / `Import:DefaultDbfEncoding`)」「性能特性 (C503 結果)」「Phase C' 申し送り」「Phase D 申し送り」「WebGIS 非影響」の 6 セクション追加
- `docs/PHASE_C_INDEX.md` を新設、本 Index へのリンク + Phase C' 申し送り Issue 候補リスト (MIF/TAB/WKT 本体収録/UcsDetectResolver) を記載
- `README.md` 「機能」セクションに「Shapefile ZIP インポート (Phase C)」1 行追加
- Phase B B601 `layer-import.md` の「Phase C 対応予定」記述を本セクションへのリンクに置換
- C503 結果がない場合 (stretch 未消化) は「性能特性は Phase C' で計測」と明記

依存: C501, C502, C503
工数: S (0.5d)

---

## Phase C' 申し送り (Issue 起票候補、本 Phase 対象外)

`PHASE_C_DESIGN_P.md` §6.12 より、`phase-c-prime-followup` ラベルで以下を別 Issue として起票予定:

1. **MIF/MID 対応** (1.5d): `GdalLayerSource` の driver 名引数 `"MapInfo File"` で活性化 + `MifPackage` 追加
2. **TAB 対応** (1.5d): 同 `"MapInfo File"` driver + `TabPackage` (`.tab/.dat/.id/.map/.ind` セット検証) 追加
3. **`IImportPackage` 抽象切り出し** (1d): 3 形式実装が揃った時点で共通化
4. **和歌山旧測地系等ローカル CS WKT 本体収録** (1.5d): `appsettings.json: Import:SridCatalog[{srid, wkt}]` 経路で外部設定化 + 実機検証
5. **`UcsDetectResolver` 実装** (0.8d): UTF-16 / EUC-JP / CP949 自動検出が必要になった時点で `IEncodingResolver` 2 実装目として追加

## Phase D 申し送り (本 Phase / Phase C' 対象外)

`PHASE_C_DESIGN_P.md` §6.13 より:

1. **KML / KMZ 対応**: LIBKML が `Minimal` SKU で利用可能か再確認、ラスタ含む KMZ の扱い
2. **GeoPackage / FGB / GPX / DXF**: `IImportPackage` 抽象確立後の driver 名 + extension 登録 1 行追加
3. **`BulkInsertMaxCount` 上限解除**: C503 結果次第で `fn_feature_bulk_insert` 専用関数の必要性を判断 (audit_log バッチ集約は **採用しない方針を堅持**)
4. **サーバ側 GDAL** (GeoServer / 内製タイラ): `GdalBootstrap` / `GdalLayerSource` をサーバ側に移植する経路を再評価
5. **ClickOnce / MSIX 差分配布**: GDAL ネイティブ DLL 60〜80MB の差分配布最適化
