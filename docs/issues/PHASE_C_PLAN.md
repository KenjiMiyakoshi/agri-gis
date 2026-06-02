# agri-gis Phase C Plan — Shapefile / MIF / TAB インポート + GDAL 投入

Phase B (`ILayerSource` 抽象 + GeoJSON/CSV インポータ + Bulk 投入経路) 完了を前提に、**Shapefile / MapInfo MIF/MID / MapInfo TAB** の 3 形式を取り込めるよう **GdalLayerSource** を追加する。API は変更最小 (`POST /api/admin/layers/{id}/features/bulk` をそのまま使う)、GDAL は WinForms 同梱とし、和歌山測地系などローカル CS の実機検証まで含める。

## スコープと前提

- 追加実装は **`GdalLayerSource : ILayerSource`** 1 本に集約。Step1 の SourceFormat ComboBox で「Phase C 対応予定」だった SHP/MIF/TAB を有効化する
- GDAL は WinForms に同梱 (`MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal`)。API/Docker は GDAL 非依存のまま
- WinForms 側で GeoJSON へ変換して既存 bulk エンドポイントへ投げる経路 (Phase B 確立済) を踏襲。新 API は追加しない
- **文字コード**: `Ogr.Open(path, options)` の `ENCODING=CP932` オプションで指定 (プロセス環境変数 `SHAPE_ENCODING` は **不採用** — xUnit 並列実行と非互換)
- **SRID 検出**: .prj / TAB の SRS から OGR `SpatialReference.AuthorityCode` を取り、`SridConverter` がサポートする SRID へ正規化。未サポート CS は WKT を `SridConverter` に登録して通す
- **MITAB 実機検証**: 和歌山測地系 (旧日本測地系系) を含むローカル CS を 1 件以上動作確認
- バイテンポラル DB 書き込みのみで完結。サーバラスタタイル化 / 選択ハイライト等の Phase D 候補要件には Phase C で触れない

## WBS

| ID | 区分 | タイトル | 見積 | 主依存 |
|---|---|---|---|---|
| C1 | WinForms | `windos-app.csproj` に `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` 追加 + 起動時 `GdalBase.ConfigureAll()` 呼び出し位置決定 + 配布サイズ確認 | S | — |
| C2 | WinForms | `GdalLayerSource : ILayerSource` 骨格 (driver 切替: ESRI Shapefile / MapInfo File [TAB] / MapInfo MIF、`ENCODING=CP932` オプション、Dispose で OGR DataSource 解放) | L | C1 |
| C3 | WinForms | Shapefile zip → temp dir 展開ヘルパ (`ShapefileZipExtractor`)。`/vsizip/` 経路を採るか実ファイル展開かは Design 論点 | M | C2 |
| C4 | WinForms | `GdalInferenceStrategy` (OGR `FieldDefn` → `InferredField`)。OFTInteger/Integer64/Real/String/Date/DateTime/Binary の写像。`IInferenceStrategy` 純粋関数化に揃える | M | C2 |
| C5 | WinForms | OGR Geometry → GeoJSON 変換 (`Geometry.ExportToJson()` ベース + MultiPolygon/Polygon 混在正規化 + Z/M 値の扱い) | M | C2 |
| C6 | WinForms | SRID 検出ロジック (`SourceSrid` プロパティ): .prj/TAB の SRS → AuthorityCode → `SridConverter.IsSupported`。未サポート時のフォールバック方針は Design 論点 | M | C2, C8 |
| C7 | WinForms | `ImportWizardForm` Step1: SourceFormat ComboBox で SHP/MIF/TAB を有効化、ファイル選択 (SHP は zip、TAB は zip or フォルダ、MIF は .mif/.mid セット) + 文字コードオプション UI | M | C2, C3 |
| C8 | WinForms | `SridConverter` 拡張: 和歌山測地系 (旧日本測地系系) などローカル CS の WKT を追加 + 動的登録 API (`RegisterWkt(int srid, string wkt)`) | S | — |
| C9 | Test | `GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` (Phase B の契約抽象クラスにフィット)。SHP/MIF/TAB 各 1 サンプルで InferSchema + ReadFeatures の同値性確認 | M | C2, C4, C5 |
| C10 | Test | 実機検証: 和歌山測地系の TAB を 1 ファイル投入し feature_current に 4326 で書き込まれるところまで E2E | S | C2, C6, C8 |
| C11 | Docs | `docs/layer-import.md` に Phase C セクション追加 (対応形式 / 文字コード方針 / GDAL ネイティブ DLL サイズ / 未サポート CS フォールバック手順) + `PHASE_C_INDEX.md` | S | C2〜C9 |

合計目安: S×4 + M×6 + L×1 ≒ 約 9〜12 人日 (Design `PHASE_B_DESIGN_P.md` §9 の「5〜7 人日」見立てより上振れ、実機検証と Wakayama 系 WKT 整備を明示計上したため)。

### Phase B 申し送り負債の合流判定 (Design で確定)

- **`BulkInsertMaxCount=5000` 上限解除**: Phase B の `appsettings.json` パラメタ化で境界テストが 2 ケースに集約済。Phase C で値を引き上げるか据え置くかは GDAL での 10 万件投入実測を見て判断 (Design 論点 9)
- **`fn_feature_bulk_insert` 専用関数**: 申し送り通り **Phase C でも採用しない** (audit_log バッチ集約方針と緊張するため)。`fn_feature_insert × N` ループを維持
- **H5 (MainForm god class) 本格分割**: Phase C スコープ外 (独立サイクル)

## 設計論点 (Design 段階で 3 案検討)

1. **Shapefile zip 展開の責務**: (a) WinForms 内 `ShapefileZipExtractor` で実 temp dir 展開 vs (b) GDAL の `/vsizip/` 仮想 FS を直接使い zip のまま開く vs (c) 専用ヘルパクラスを `Services/Import/Vfs/` 配下に切る。.cpg / .prj まで含めた可搬性と OGR の挙動安定性のトレードオフ
2. **文字コード判定**: (a) CP932 固定 (日本市場前提) vs (b) `.cpg` 同梱優先 + 無ければ CP932 フォールバック vs (c) Step1 でユーザに ComboBox 選択 (UTF-8/CP932/Shift_JIS/CP1252)。MIF はヘッダの `CharSet` 句、TAB は `Tab File` のメタ情報を別途見るか
3. **SRID 検出失敗時のフォールバック**: (a) デフォルト 4326 を黙って採用 vs (b) Step1 でユーザに手動 SRID 指定を要求し空欄なら投入不可 vs (c) `SourceSrid=null` のまま Step2 へ進め Step3 直前に確認ダイアログ。`SridConverter.IsSupported` 通過可否で分岐する案も
4. **OGR FieldDefn → InferredField マッピング**: (a) OFT 型に完全準拠 (Integer/Integer64=integer, Real=number, String=string, Date/DateTime=date, Binary=未サポート) vs (b) Phase B CsvInferenceStrategy と同じく実値サンプリングで boolean/date を再推定 vs (c) 信頼度フィールドを `InferredField` に追加してユーザ編集を促す
5. **大規模 Shapefile (10 万件以上) のメモリ管理**: (a) OGR `Layer.GetNextFeature()` ループを `IAsyncEnumerable<GeoJsonFeature>` で逐次 yield (1 件単位、Phase B Chunker が 1000 件に束ねる) vs (b) GDAL 側で SQL `LIMIT/OFFSET` ページング vs (c) WinForms 側で chunk 単位の `using` スコープを切り Feature を明示 Dispose
6. **GDAL ネイティブ DLL サイズと配布戦略**: (a) `WindowsRuntime.Minimal` 同梱 (展開後数十 MB、配布 zip に直接含める) vs (b) 別途インストーラ + 起動時ダウンロード vs (c) ClickOnce / MSIX で差分配布。CI/インストーラ整備の労力差
7. **ImportWizardForm Step1 UI の有効化方法**: (a) Phase B の「Phase C 対応予定」ラベルを削除 + SourceFormat ComboBox の全項目を活性化 vs (b) Feature Flag (`appsettings:Features:GdalImport=true`) で段階リリース vs (c) admin 設定で形式単位の ON/OFF
8. **ジオメトリ型混在の正規化**: (a) Polygon 混在 Shapefile は MultiPolygon に昇格 (1 リング化) vs (b) レイヤ作成時に `geometry_type=Polygon` で固定し MultiPolygon は分割 → 複数 feature 化 vs (c) feature 単位で `MultiPolygon` 固定 (DB の `feature_current.geom` が `geometry(Geometry, 3857)` 固定なので影響小)
9. **`BulkInsertMaxCount` 上限解除を同 Phase で扱うか**: (a) Phase C スコープ外 (Phase B 申し送り通り据え置き 5000) vs (b) GDAL 実測後 50000 まで引き上げる vs (c) chunk サイズと別軸で「投入ジョブ全体の上限」を設定値追加
10. **和歌山測地系等ローカル SRS のサポート方針**: (a) `SridConverter` に WKT 直書きで 1〜2 件のみ追加 vs (b) EPSG コードを持つもののみ受け入れる vs (c) proj 文字列を appsettings に並べて動的登録 (`RegisterWkt` / `RegisterProj`)。和歌山測地系は EPSG コードを持たないケースがあるため (a)/(c) が現実的
11. **Shapefile (.dbf) と TAB (.mif/.tab) の文字コード差異**: (a) 両形式で同じ `ENCODING` オプションを使う vs (b) Shapefile は `ENCODING=CP932`、TAB は MITAB 既定 + `CharSet` パース vs (c) 形式ごとに独立した `EncodingResolver` を切る
12. **MITAB ドライバの 64bit/32bit 環境差**: (a) win-x64 固定で配布 vs (b) AnyCPU + 実行時にビット数別 native dll を読む vs (c) WinForms アプリ全体を x64 固定 (`<PlatformTarget>x64</PlatformTarget>`)。`MaxRev.Gdal.WindowsRuntime.Minimal` は x64 のみ提供のため (a)/(c) が候補
13. **部分的に読めない feature のエラーハンドリング**: (a) skip + WARN ログ + Step3 完了ダイアログにスキップ件数表示 vs (b) fail-fast (1 件目で `ImportException` 投げジョブ全体ロールバック) vs (c) skip 上限 (例 1%) を超えたら fail-fast に切替
14. **OGR Geometry → GeoJSON 変換経路**: (a) OGR 標準 `Geometry.ExportToJson()` (依存最小、文字列経由) vs (b) NetTopologySuite を導入し `WKB → NTS → GeoJsonWriter` (型安全、Z/M 扱い良) vs (c) WKT 経由 (依存最小だがパース二度手間)。Phase B が System.Text.Json 直書き方針なので (a) が整合的
15. **`GdalBase.ConfigureAll()` の呼び出し位置**: (a) `Program.Main` 先頭 (起動コスト顕在化) vs (b) 初回 `GdalLayerSource` 構築時に `Lazy<>` で遅延 vs (c) DI コンテナの Singleton 登録時。テスト並列実行時の `static` 状態汚染リスクと起動時間のトレードオフ

## 読むべきファイル (Design/Review エージェント向け)

### Phase B 確立済の Import 基盤
- `windos-app/Services/Import/ILayerSource.cs` — `SourceFormat / SourceSrid / InferSchemaAsync / ReadFeaturesAsync(targetSrid, ct)` 契約。Phase C は本契約を厳守
- `windos-app/Services/Import/GeoJsonLayerSource.cs` / `CsvLayerSource.cs` — 既存 2 実装。`GdalLayerSource` の形を揃える参考
- `windos-app/Services/Import/InferenceStrategies/IInferenceStrategy.cs` / `GeoJsonInferenceStrategy.cs` / `CsvInferenceStrategy.cs` — 純粋関数化の流儀。`GdalInferenceStrategy` を同じ規約で追加
- `windos-app/Services/Import/SridConverter.cs` — 4326/4612/6668/3857 をキャッシュする ProjNet ラッパ。Phase C で和歌山測地系等の WKT 追加点
- `windos-app/Services/Import/Chunker.cs` / `GeoJsonFeature.cs` / `InferredField.cs` — chunk 投入経路 (Phase C は触らない想定)

### ウィザード UI
- `windos-app/Forms/ImportWizardForm.cs` — Step1 SourceFormat ComboBox / ファイル選択 / 「Phase C 対応予定」ラベル箇所 (C7 で解除)
- `windos-app/ViewModels/ImportWizardViewModel.cs` — `INotifyPropertyChanged` 純粋関数化。SHP/MIF/TAB 用 options プロパティの追加点
- `windos-app/Forms/LayerAdminForm.cs` — `ImportWizardForm` の起動導線

### 契約テスト基盤
- `windos-app.tests/Tests/Services/Import/ILayerSourceContractTests.cs` — ジェネリック抽象テストクラス。`GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` を追加するだけで契約自動検証
- `windos-app.tests/Fixtures/import/` — Phase C で SHP zip / MIF+MID / TAB zip / 和歌山測地系サンプルを追加

### Phase B Design / 申し送り
- `docs/issues/PHASE_B_DESIGN_P.md` §9 (Phase C 申し送り) — `GdalLayerSource` 追加・`ENCODING=CP932` Open オプション方針・和歌山系実機検証 1 人日・`BulkInsertMaxCount` 上限解除判断・`fn_feature_bulk_insert` 不採用方針
- `docs/issues/PHASE_B_DESIGN_A.md` §5.4 — 案 A の GDAL UI フロー / `/vsizip/` 経路 / OGR→GeoJSON 変換シーケンスのスケッチ (C2/C3/C5 の参考)
- `docs/layer-import.md` — Phase B 出荷時の対応形式記述。Phase C で SHP/MIF/TAB セクションを追記

### DB / API (Phase C は変更しない想定だが参照)
- `db/init/001_init.sql` — `feature_current.geom geometry(Geometry, 3857)` 制約 (論点 8 のジオメトリ型混在に関連)
- `db/migration/006_fn_feature_insert.sql` — bulk ループのターゲット関数
- `api/Endpoints/AdminLayersEndpoints.cs` — `POST /api/admin/layers/{id}/features/bulk` (Phase B 実装)。Phase C は呼ぶだけで変更しない

## Phase A/B 完了済前提のサマリ (Phase C Design に直接効く資産)

- **認証/認可**: JWT Bearer + 3 ロール (admin/general/guest)。`/api/admin/*` は admin 限定。Phase C で UI 解禁する SHP/MIF/TAB インポートも `LayerAdminForm` 配下なので admin のみ
- **`ILayerSource` 抽象**: `SourceFormat / SourceSrid / InferSchemaAsync / ReadFeaturesAsync(targetSrid, ct)` で形式差を吸収。Phase C は新実装 1 本追加だけで Step1〜Step3 経路に乗る
- **`IInferenceStrategy` 純粋関数化**: テスト容易、副作用なし。`GdalInferenceStrategy` を同方針で追加
- **`SridConverter`**: ProjNet ラッパで 4326/4612/6668/3857 をキャッシュ。Phase C で WKT 追加点が明確
- **`Chunker` + Bulk 投入経路**: 1 chunk = 1 Tx、1000 件チャンク、`BulkInsert.MaxCountPerChunk=5000` (`appsettings.json`)。Phase C は OGR Feature を `IAsyncEnumerable<GeoJsonFeature>` で yield するだけで自動的に乗る
- **`LayerAdminForm` + `ImportWizardForm` + `ImportWizardViewModel`**: Step1 SourceFormat / Step2 SchemaGrid / Step3 投入実行の三段構成。Phase C は Step1 のラベル解除と options 追加が中心
- **`ILayerSourceContractTests<T>`**: ジェネリック抽象テスト。`GdalLayerSourceTests` を継承する 1 ファイル追加で契約一貫性を自動検証
- **DB / API スキーマ**: `layers` (layer_type / geometry_type / source_format / source_srid / description / created_by / created_org_id / deleted_at) / `audit_log` (actor_user_id NOT NULL、geom_geojson 除外済) / `fn_feature_insert` / `fn_layer_create` / `fn_layer_delete` / `layer_import_job` (start/finalize/GET)。Phase C で追加変更なし
- **Phase B 申し送り**: `ENCODING=CP932` は Open オプション方式、`fn_feature_bulk_insert` 不採用、`BulkInsertMaxCount` 据え置き判断は Phase C 実測後、和歌山系実機検証 1 人日を明示計上

## 次ステップ

Design 段階では上記 15 論点について 3 案 (P/Q/R) を起こし、特に **論点 1 (zip 展開経路)・論点 2 (文字コード)・論点 3 (SRID フォールバック)・論点 5 (大規模メモリ管理)・論点 10 (ローカル SRS サポート)** はインタフェース署名や `SridConverter` API に波及するため Design 確定が必須。WBS の **C2 (L)** と **C7 (M)** は GDAL ネイティブ依存リスクが集中するので Design で OGR 呼び出しシーケンス + Step1 UI モックをスケッチしておくこと。
