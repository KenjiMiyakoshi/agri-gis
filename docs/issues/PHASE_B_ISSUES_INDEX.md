# agri-gis Phase B イシュー一覧 (案 P)

`PHASE_B_DESIGN_P.md` 分割。`B1xx-B6xx`: DB/API/空/WinForms/Test/Docs。S=0.5d/M=1d/L=2d。

## 一覧 (25 Issue / 19.5d)

| # | タイトル | 工数 | P | 依存 |
|---|---|---|---|---|
| B101 | layers 拡張 | M | P101 | — |
| B102 | fn_layer_create | S | P102 | B101 |
| B103 | fn_layer_delete | S | P103 | B101 |
| B104 | layer_import_job | S | P104 | B101 |
| B201 | JsonOpts(H2) + 雛形 + DTO | M | P201前 | B102 |
| B202 | LayerAdmin CRUD + 認可 | M | P201後 | B201,B103 |
| B203 | bulk + MaxCount + 413 | M | P202前 | B201,B202 |
| B204 | import-jobs + 409 | M | P202後 | B201,B104 |
| B205 | FeatureEndpoints deleted_at | S | P203 | B103 |
| B401 | NuGet + ApiClient 拡張 | S | — | B202 |
| B402 | ILayerSource + GeoJson/Csv | M | P303 | B401 |
| B403 | IInferenceStrategy | M | P304前 | B401 |
| B404 | SridConverter + Chunker | S | P305 | B402 |
| B405 | SchemaGrid(H4) + AttributeEditor | M | P304後 | B403 |
| B406 | LayerAdminForm + MainForm | M | P301 | B202,B401 |
| B407 | ImportWizardViewModel | M | P302前 | B402,B403 |
| B408 | ImportWizardForm | L | P302後 | B404-B407 |
| B501 | 認可[Theory] + audit + 409/413 | M | P401 | B202-B204 |
| B502 | FeatureEndpoints deleted_at 回帰 | S | — | B205 |
| B503 | ILayerSourceContractTests<T> | S | P402前 | B402 |
| B504 | Inference/Srid/Chunker [Theory] | S | P402中 | B403 |
| B505 | ViewModel ヘッドレス | M | P402後 | B407 |
| B506 | 5000 件投入スパイク | S | P403 | B203,B204 |
| B601 | docs/layer-import.md | M | P501前 | B408 |
| B602 | auth.md + PHASE_B_INDEX | S | P501後 | B202,B601 |

クリティカルパス: B101→B102→B201→B202→B203→B501→B601→B602 (≈10〜12d)。ラベル: `phase:B` / `area:db|api|winforms|tests|docs` / `negotiable-debt:H2`(B201)/`H4`(B405) / `stretch`(B506)。全 PR `base=main` (`stacked_pr_pitfall`)。推奨着手: B506→B101→B102/B103/B104 並列→B201→WinForms 並列→B408→Test/Docs。

---

## Issue 詳細

### B101 layers 拡張 (DB/M/P101)
`geometry_type` を `layer_type` から分離 (案 A 先食い)、`created_org_id`/`source_format`/`source_srid`/`description`/`deleted_at`/`updated_at`/`created_by` 追加。
- `0B01_layers_extend.sql` 2 回適用安全
- `source_format` CHECK `('geojson','csv','shapefile','mif','tab') OR NULL`
- `geometry_type` CHECK (Point/LineString/Polygon/Multi*/Collection)
- `created_by UUID FK`/`created_org_id INT FK`、既存は seed admin でバックフィル

### B102 fn_layer_create (DB/S/P102)
layers→layer_schema_version→audit_log 3 段関数。
- `0B02_fn_layer_create.sql`、戻り値=layer_id
- `audit_log.after_doc` から geom 除外 (C2 継承)
- `p_actor`=display_name / `p_user_id`=UUID (A106)、`p_schema_json` NULL で `[]`

### B103 fn_layer_delete (DB/S/P103)
`layers.deleted_at=now()` のみ、feature_current 据え置き (案 C 論点 5)。
- `0B03_fn_layer_delete.sql`、既削除で例外
- `audit_log layer_delete` 1 行 (before_doc)、feature_current 行数不変

### B104 layer_import_job (DB/S/P104)
同期完結のまま 1 行だけ書く観測基盤 (拡張性補強 1)。
- `job_id UUID PK`/`layer_id FK`/`status CHECK IN ('running','succeeded','failed')`
- `total_count`/`inserted_count`/`started_at`/`finished_at`/`error_text`/`created_by FK`/`created_org_id`
- `0B04_layer_import_job.sql` 2 回適用安全

### B201 JsonOpts(H2)+雛形+DTO (API/M/P201前) `negotiable-debt:H2`
`api/Json/JsonOpts.cs` で H2 解消 + `MapGroup("/api/admin/layers").RequireRole("admin")` 雛形+DTO。
- `JsonOpts.Default`/`WithGeoJson` 公開、grep 0 件まで置換
- `LayerAdminDto`/`Create/UpdateLayerRequestDto`/`BulkFeaturesReq/RespDto`/`ImportJobDto`
- `AdminLayersEndpoints.cs` 雛形 + `Program.cs MapAdminLayers()`

### B202 LayerAdmin CRUD (API/M/P201後)
5 endpoint。`GET ?includeDeleted=false` デフォルト、`DELETE` は `fn_layer_delete`+409 フック。
- GET 200/403、POST 201+Location、GET{id} 200/404、PATCH 200/400/404、DELETE 204/404/409
- 既存 `PUT .../schema` 無傷 (案 C 論点 12)、A506 既存テスト green

### B203 bulk+413 (API/M/P202前)
Tx 内 `fn_feature_insert × N`、`p_request_id` チャンク単位 UUID。専用バルク関数なし。
- `appsettings: BulkInsert {MaxCountPerChunk:5000, ChunkDefaultSize:1000}` + `IOptions`
- `Count>Max` で 413 ProblemDetails、1 chunk=1 Tx+1 request_id+N 回
- レスポンス `{insertedCount, featureIds[]}`

### B204 import-jobs+409 (API/M/P202後)
start/finalize/GET の 3 endpoint。同 layer `status='running'` 中 start 409 (実装リスク 6)。
- start 201/409、finalize 200/404/409、GET 200/404
- B202 DELETE と `running` 判定共通化、`failed` 時 `error_text` 保存

### B205 FeatureEndpoints deleted_at (API/S/P203)
案 C 致命 3。SQL に `l.deleted_at IS NULL` 追加。
- 全 SELECT で確認、欠落箇所追記 (コミットに前後 SQL)、B502 green

### B401 NuGet+ApiClient (WinForms/S)
`NetTopologySuite.IO.GeoJSON4STJ`+`ProjNet` (GDAL 非投入)。
- csproj 参照+`dotnet restore` 成功
- `ListLayersAdmin`/`Create/Update/DeleteLayer`/`Start/Finalize/GetImportJob`/`BulkInsertFeatures`
- A403 `BearerHandler` 経由、401/403/409/413 区別 `ApiException`

### B402 ILayerSource (WinForms/M/P303)
`IAsyncEnumerable<>`/`Task<>` 統一 (実装リスク 8)。
- `ILayerSource:IAsyncDisposable` (`SourceFormat`/`SourceSrid`/`InferSchemaAsync`/`ReadFeaturesAsync(int,ct)`)
- `GeoJsonLayerSource` SRID=4326、`CsvLayerSource` lat/lng ctor 指定、Phase C 追加でゼロ破壊

### B403 IInferenceStrategy (WinForms/M/P304前)
共通-S1。100% メモリ完結純粋関数。
- `InferAsync(Stream,ct)→Task<IReadOnlyList<InferredField>>`、DB/API 依存ゼロ
- `InferredField`→`SchemaFieldDto` 縮退ヘルパ
- null/空文字混在で `nullable=true`、ISO8601 で `date`、整数のみで `integer`

### B404 SridConverter+Chunker (WinForms/S/P305)
CSV 4326 化+chunk 化。
- `SridConverter.To4326(srid,x,y)→(lon,lat)` 純粋関数
- 既知 SRID (4612/6668/3857/30169-30179) キャッシュ
- `Chunker.ChunkAsync<T>(IAsyncEnumerable<T>,int)→IAsyncEnumerable<IReadOnlyList<T>>`

### B405 SchemaGrid(H4)+AttributeEditor (WinForms/M/P304後) `negotiable-debt:H4`
UserControl 共通化、`ParentForm` キャストを `IFeatureSaveCoordinator` 経由に。
- `Controls/SchemaGrid.cs`+Designer、`Fields` で `BindingList<InferredField>` バインド
- `IFeatureSaveCoordinator` でキャスト置換、既存利用箇所無傷

### B406 LayerAdminForm+MainForm (WinForms/M/P301)
Modal 起動、MainForm 追加 1 行のみ (H5 持ち越し)、admin 以外 Visible=false (案 C 論点 11)。
- `Show()` 起動、MainForm 「レイヤ管理」1 行追加
- admin 以外 Visible=false (サーバ `RequireRole` と 2 重防御)、ToolStrip [新規/編集/削除]

### B407 ImportWizardViewModel (WinForms/M/P302前)
C-S2 部分採用。`INotifyPropertyChanged` で純粋関数化。
- B505 ヘッドレス可能 (UI スレッド非依存)
- `CurrentStep`/`CanGoNext`/`CanGoBack`/`IsImporting`/`Progress` 公開
- `EnterNext/PreviousStep()` 明示、`LastError` 記録 (例外 Form 漏れなし)

### B408 ImportWizardForm (WinForms/L/P302後)
Step1(GeoJSON/CSV のみ、SHP/MIF/TAB は「Phase C 対応予定」)→Step2 SchemaGrid→Step3 start→chunk→finalize。
- Wizard 遷移動作、CSV で `SridConverter` 4326 化
- chunk `ChunkDefaultSize` 参照、ProgressBar 更新、キャンセル時 `failed`+DELETE 確認

### B501 認可+audit+409/413 (Test/M/P401)
A505 `[Theory]` 継承。`admin/general/guest × CRUD+bulk = 15 ケース`。
- 15 ケース以上の認可マトリクス
- `audit_log layer_create/delete` 行、geom 非含 (C2 回帰)
- `running` 中 DELETE 409、start 二重 409、`Count` 超過 413

### B502 FeatureEndpoints 回帰 (Test/S)
論理削除 layer の feature が見えない (案 C 致命 3 回帰)。
- 作成→feature 3 件→`fn_layer_delete`→`GET /api/features?layerId={id}` 0 件 (or 404)
- 既存 `AsOfTests` green、`?includeDeleted=true` は実装なら 3 件、しないなら申し送り

### B503 ContractTests<T> (Test/S/P402前)
B-S1。ジェネリック抽象で Phase C `Gdal` 追加時に契約自動検証。
- 5 件以上の契約 (非空 / 順次返却 / Dispose 後例外)
- Fixture `Fixtures/import/geojson_point.json`+`csv_latlng.csv` 共用、Linux ランナー OK

### B504 純粋関数[Theory] (Test/S/P402中)
Inference+Srid+Chunker。
- 形式別 10 ケース以上、`SridConverter` 主要 SRID (4612/6668/30172)
- `Chunker` 境界 (0/倍数/端数)、全テスト 1 秒以内

### B505 ViewModel ヘッドレス (Test/M/P402後)
`Mock<IApiClient>` で Step/ガード/Progress/LastError 検証。
- Step1 未選択時 `CanGoNext=false`、Step3 中 `IsImporting=true`/`CanGoBack=false`
- chunk 失敗で `LastError`、Mock N 回検証、`Application.Run` 不要

### B506 5000 件スパイク (Test/S/P403) `stretch`
実装リスク 4。`[Trait("Category","Performance")]` で計測。
- `db/fixtures/perf/sample_5000.geojson` 生成、所要時間+GC を Console 出力
- CI で Trait 除外、結果を B601 へ、30 秒超で `ChunkDefaultSize=500` 推奨

### B601 layer-import.md (Docs/M/P501前)
Phase B 範囲+Phase C 申し送り+GDAL サイドカー+`SHAPE_ENCODING` Open オプション (C-地雷 1 回避)。
- 新設、形式+申し送り表、QGIS GeoJSON 変換暫定回避策
- B506 結果を「性能特性」、`BulkInsertMaxCount`/`ChunkDefaultSize` 運用ガイド

### B602 auth.md+PHASE_B_INDEX (Docs/S/P501後)
- `docs/auth.md` に `/api/admin/layers*` Phase B endpoint (admin only)
- `docs/PHASE_B_INDEX.md` 概要/シナリオ/リンク 1 枚、`README.md`「機能」に 1 行追加

---

Phase C 申し送り: `PHASE_B_DESIGN_P.md` §9 参照 (GdalLayerSource / SHAPE_ENCODING Open オプション / MIF・TAB 実機検証 / 上限解除 / Docker サイドカー / H5 分割 / 非同期化)。
