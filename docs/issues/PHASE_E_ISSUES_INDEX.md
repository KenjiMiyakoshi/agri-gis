# agri-gis Phase E イシュー一覧 (案 P)

`PHASE_E_DESIGN_P.md` 分割。`E1xx`=PoC/DB / `E2xx`=API / `E3xx`=GeoServer / `E4xx`=UI / `E5xx`=Tests/Docs。S=0.3d / M=0.5-0.7d。

## 一覧 (17 Issue / 約 10.0d)

| # | タイトル | 工数 | 主担当 | 依存 |
|---|---|---|---|---|
| E100 | `feature_asof` view 性能 PoC (50 万件 fixture × z=15 < 500ms) (Gate) | M(1.0d) | infra/db | — |
| E101 | DB migration `0E01_layer_history.sql` (Phase A feature_history 同型) | S(0.3d) | db | E100 |
| E102 | DB migration `0E02_layers_valid_from_to.sql` (列追加 + backfill) | S(0.4d) | db | E100 |
| E103 | DB migration `0E03_layer_style_version.sql` (layer_schema_version 同型) | S(0.3d) | db | E100 |
| E104 | DB function `0E04_fn_layer_update.sql` (PATCH 用、楽観ロック + history 退避 + audit_log) | M(0.7d) | db | E101, E102 |
| E105 | DB function `0E05_fn_layer_delete_v2.sql` (deleted_at + valid_to 二重書き) | M(0.5d) | db | E101, E102 |
| E106 | DB function `0E06_fn_layer_style_upsert.sql` + `0E07_feature_asof_view.sql` | M(0.5d) | db | E103 |
| E201 | api shared `AsOfParser` 共通化 + `GET /api/layers?asOf=` | S(0.5d) | api | E101-E106 |
| E202 | `GET /api/admin/layers?asOf=` + PATCH/DELETE を `fn_layer_update/delete v2` 経由化 | M(0.6d) | api | E201, E104, E105 |
| E203 | `GET/PUT /api/admin/layers/{id}/style?asOf=` + PUT を `fn_layer_style_upsert` 経由化 | M(0.5d) | api | E201, E106 |
| E204 | `GET /api/layers/{id}/extent?asOf=` + `GET /api/layers/{id}/at?asOf=` (feature_asof view 経由) | S(0.3d) | api | E201, E106 |
| E205 | `GET /tiles/.../?asOf=` 分岐 (feature_asof featureType + Cache-Control: no-store) | S(0.3d) | api | E201 |
| E301 | `tools/geoserver-setup/setup.ps1` 拡張: `feature_asof` featureType 自動公開 + GeoServer reload | M(0.6d) | infra | E106 |
| E302 | `TilesEndpoints.cs` の asOf 分岐実装 + 動作確認 | S(0.4d) | api | E205, E301 |
| E401 | webgis: tileLayer URL builder に `asOf` 追加 + `getFeaturesAt` asOf 引数 + main.ts/bridge 配線 | M(0.7d) | webgis | E302 |
| E402 | windos-app: MainForm `DateTimePicker asOfPicker` + ApiClient asOf 引数 + AttributeEditor disable | M(0.8d) | winforms | E401 |
| E501 | api.tests 新規 5 件 (LayerAsOfTests / StyleHistoryTests / TilesAsOfTests / AsOfParserTests / LayerUpdateBitemporalTests) | M(0.6d) | tests | E402 |
| E502 | webgis vitest 新規 2 件 (tileLayerAsOf / selectionAsOf) | S(0.3d) | tests | E401 |
| E503 | windos-app.tests 新規 1 件 (MainFormAsOfPickerTests) | S(0.3d) | tests | E402 |
| E504 | e2e smoke + `docs/bitemporal-asof.md` + `docs/PHASE_E_INDEX.md` 最終化 | M(0.8d) | docs | E501-E503 |

(20 Issue。E100 がエントリ → E101-E106 (DB) → E201-E205 (API) → E301-E302 (GeoServer) → E401-E402 (UI) → E501-E504 (Tests + Docs))

クリティカルパス: E100 → E102 → E104 → E202 → E302 → E402 → E501 → E504 (≒ **7.5 営業日 + バッファ**)

ラベル: `phase:E` / `area:db|api|webgis|winforms|tests|docs|infra` / `wave:WE0`〜`wave:WE5` / `phase-e-prime-followup` (`deleted_at` DROP / パーティショニング / 本番 GeoServer setup.ps1 等の申し送り)

全 PR `base=main` 固定 (MEMORY.md `stacked_pr_pitfall`)。

---

## Issue 詳細

### E100 `feature_asof` view 性能 PoC (Gate)

50 万件 fixture (Phase D D504 で使った `sample-shp-generator` を 1000 倍化) を `feature_current` に投入 + 仮 `feature_history` に過去版 100 万件追加 → `feature_asof` view 経由で z=15 タイル取得 5 回平均が `< 500ms` (cold) / `< 50ms` (warm) を満たすか確認。

受け入れ条件:
- `tools/perf/feature-asof-50k/generate.sh` で fixture 生成スクリプト
- `tools/perf/feature-asof-50k/run.ps1` で curl 性能計測
- 5 リクエスト平均応答時間を `docs/issues/PHASE_E_E100_POC_RESULT.md` に記録
- `> 2s` の場合は Phase E 着手中止 (no-go) → パーティショニング戦略確定 (Q1) or Phase E スコープ縮小
- `< 500ms` で go 判定、WE1 着手承認

### E101 0E01_layer_history.sql

`db/migration/0E01_layer_history.sql` 新規。Phase A `feature_history` (`db/migration/003_feature_history.sql`) と同形のテーブル。

受け入れ条件:
- `layer_history(history_id BIGSERIAL PK, layer_id INT, layer_name TEXT, layer_type TEXT, geometry_type TEXT, description TEXT, source_format TEXT, source_srid INT, schema_version INT, schema_json JSONB, style_json JSONB, owner_org_id INT, is_shared BOOL, created_by UUID, created_org_id INT, version INT, valid_from DATE, valid_to DATE, created_at TIMESTAMPTZ, updated_at TIMESTAMPTZ, archived_at TIMESTAMPTZ DEFAULT now())`
- `CREATE INDEX ix_layer_history_layer_id_valid ON layer_history(layer_id, valid_from, valid_to)`
- down script: `DROP TABLE IF EXISTS layer_history CASCADE`
- 既存 layers 行を初期 history に backfill **しない** (Phase E 着手前の履歴は遡及しない、将来データのみ履歴化)

### E102 0E02_layers_valid_from_to.sql

`db/migration/0E02_layers_valid_from_to.sql` 新規。`layers` テーブルに `valid_from/_to/version` 列追加 + 既存 `deleted_at IS NOT NULL` 行の `valid_to` backfill。

受け入れ条件:
- `ALTER TABLE layers ADD COLUMN IF NOT EXISTS valid_from DATE NOT NULL DEFAULT CURRENT_DATE`
- 同 `valid_to DATE NOT NULL DEFAULT '9999-12-31'::date`
- 同 `version INTEGER NOT NULL DEFAULT 1`
- `UPDATE layers SET valid_to = deleted_at::date WHERE deleted_at IS NOT NULL`
- `valid_from <= valid_to` の CHECK 制約追加
- down script: `ALTER TABLE layers DROP COLUMN ...` 3 列削除

### E103 0E03_layer_style_version.sql

`db/migration/0E03_layer_style_version.sql` 新規。`layer_schema_version` (`db/migration/005_layer_schema_version.sql`) と同形。

受け入れ条件:
- `layer_style_version(layer_id INT FK layers, style_version INT, style_json JSONB, valid_from DATE, valid_to DATE, created_by UUID FK users, created_at TIMESTAMPTZ, PRIMARY KEY (layer_id, style_version))`
- `CREATE INDEX ix_layer_style_version_active ON layer_style_version(layer_id, valid_from, valid_to)`
- 既存 layers.style_json 行から `style_version=1` で初期 INSERT (`SELECT layer_id, 1, style_json, CURRENT_DATE, '9999-12-31'::date, NULL, now() FROM layers WHERE valid_to='9999-12-31'`)
- down script: `DROP TABLE IF EXISTS layer_style_version CASCADE`

### E104 0E04_fn_layer_update.sql

`db/migration/0E04_fn_layer_update.sql` 新規。Phase A `fn_feature_update` 流儀の `fn_layer_update`。

受け入れ条件:
- シグネチャ: `(p_layer_id INT, p_layer_name TEXT, p_layer_type TEXT, p_geometry_type TEXT, p_description TEXT, p_source_format TEXT, p_source_srid INT, p_expected_version INT, p_actor TEXT, p_request_id UUID, p_user_id UUID, p_org_id INT) RETURNS TABLE(layer_id INT, version INT)`
- 楽観ロック: `WHERE layer_id=p_layer_id AND valid_to='9999-12-31'::date AND version=p_expected_version`、不一致時 `RAISE EXCEPTION 'optimistic_lock_violation'`
- 旧行を `layer_history` に INSERT (`valid_to=CURRENT_DATE`, `archived_at=now()`)
- 新行で `layers` UPDATE (`valid_from=CURRENT_DATE`, `version=p_expected_version+1`, 更新列 + `updated_at=now()`)
- `audit_log` INSERT (`actor`, `actor_user_id=p_user_id`, `before_doc`, `after_doc`, `meta_jsonb={layer_id, version_before, version_after}`)
- down script: `DROP FUNCTION IF EXISTS fn_layer_update(...)`

### E105 0E05_fn_layer_delete_v2.sql

`db/migration/0E05_fn_layer_delete_v2.sql` 新規。既存 `fn_layer_delete` を `CREATE OR REPLACE` で置換。

受け入れ条件:
- シグネチャは既存と同じ (`(p_layer_id INT, p_actor TEXT, ...) RETURNS VOID`)
- 旧行を `layer_history` に退避 (`valid_to=CURRENT_DATE`)
- `layers` を `UPDATE SET deleted_at=now(), valid_to=CURRENT_DATE`
- `audit_log` INSERT (op='delete')
- 二重書き (deleted_at + valid_to) を Phase E で固定、Phase E' で deleted_at 廃止
- down script: 旧 `fn_layer_delete` 定義に戻す (`CREATE OR REPLACE` で復元)

### E106 0E06_fn_layer_style_upsert.sql + 0E07_feature_asof_view.sql

2 ファイルセット。

受け入れ条件:
- 0E06: `fn_layer_style_upsert(p_layer_id INT, p_style_json JSONB, p_actor TEXT, p_request_id UUID, p_user_id UUID, p_org_id INT) RETURNS TABLE(layer_id INT, style_version INT)`
- 新 `style_version = MAX(style_version)+1` (なければ 1)
- 旧 active 行 `UPDATE valid_to=CURRENT_DATE WHERE layer_id=p_layer_id AND valid_to='9999-12-31'`
- 新行 INSERT
- `layers.style_json` も同期更新 (current value 冗長保持)
- `audit_log` INSERT
- 0E07: `CREATE OR REPLACE VIEW feature_asof AS SELECT ... FROM feature_current UNION ALL SELECT ... FROM feature_history`
- down: `DROP FUNCTION ...` + `DROP VIEW IF EXISTS feature_asof`

### E201 AsOfParser shared + GET /api/layers?asOf=

`api/Shared/AsOfParser.cs` 新規 (既存 `FeatureEndpoints.ParseAsOf` を移植 + テスト追加)。`LayerEndpoints.MapGet("/")` で `?asOf=` 受領。

受け入れ条件:
- `AsOfParser.TryParse(string? asOf) → DateOnly?` (null = 現在)
- ISO datetime は `ValidationException(asOf, "iso_date_only", "asOf must be ISO date YYYY-MM-DD")`
- `GET /api/layers?asOf=2025-01-01` で `layers + layer_history` UNION ALL
- `GET /api/layers` (asOf 無し) は `WHERE valid_to='9999-12-31'`
- 既存 `?layerId=` 410 動作維持

### E202 AdminLayersEndpoints asOf + PATCH/DELETE 関数化

受け入れ条件:
- `GET /api/admin/layers?asOf=` 対応 (`includeDeleted` と排他、asOf 指定時は `includeDeleted` 無視)
- `PATCH /api/admin/layers/{id}` の SQL を `fn_layer_update(...)` 呼出に置換
- `DELETE /api/admin/layers/{id}` の SQL を `fn_layer_delete v2` 呼出に置換 (関数名は同名で CREATE OR REPLACE 済)
- 楽観ロック (`If-Match: {version}` ヘッダ) を `fn_layer_update` の `p_expected_version` に伝搬
- 409 (`optimistic_lock_violation`) を ProblemDetails で返す

### E203 AdminLayerStyleEndpoints asOf + PUT 関数化

受け入れ条件:
- `GET /api/admin/layers/{id}/style?asOf=` 対応 (`layer_style_version` SELECT)
- `PUT /api/admin/layers/{id}/style` の内部 SQL を `fn_layer_style_upsert(...)` 呼出に置換
- GeoServer 同期 (Phase D D203 の `IGeoServerStyleSync`) は **Tx 内で続行**: DB 履歴 append → GeoServer push → 失敗時 DB rollback (Phase D 挙動維持)

### E204 layers/{id}/extent + at asOf

受け入れ条件:
- `GET /api/layers/{id}/extent?asOf=` 対応 (`feature_asof` view + `valid_from <= @asof AND @asof < valid_to`)
- `GET /api/layers/{id}/at?asOf=` 対応 (同上、ST_DWithin)
- asOf 無しは既存通り `feature_current` 直接

### E205 TilesEndpoints asOf

受け入れ条件:
- `GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png?asOf=YYYY-MM-DD` 受領
- asOf 無し: `agrigis:feature_current` featureType + `CQL_FILTER=layer_id={N}` + `Cache-Control: max-age=3600` (既存)
- asOf あり: `agrigis:feature_asof` featureType + `CQL_FILTER=layer_id={N} AND valid_from <= '{asOf}' AND '{asOf}' < valid_to` + `Cache-Control: no-store`
- TilesEndpoints の URL ビルドロジックを `if (asOf.HasValue) { feature_asof } else { feature_current }` で分岐

### E301 setup.ps1 拡張: feature_asof featureType + GeoServer reload

受け入れ条件:
- `tools/geoserver-setup/setup.ps1` に featureType `feature_asof` の POST 追加
- 既存 featureType `feature_current` と同じ datastore (`postgis_main`)、source は PostgreSQL VIEW
- POST 成功後 `POST /rest/reload` で GeoServer 再ロード
- 既存 setup.ps1 の idempotent 性 (409 既存 → 続行) を `feature_asof` にも適用
- 動作確認: WMS GetMap で `feature_asof` から PNG 取得成功

### E302 TilesEndpoints の asOf 分岐実装 + 動作確認

E205 で実装した分岐の動作確認 Issue (実装は E205 に含まれる場合は本 Issue は smoke test に縮退)。

受け入れ条件:
- `GET /tiles/.../?asOf=2025-01-01` 200 PNG + `Cache-Control: no-store`
- `GET /tiles/...` (asOf 無し) 200 PNG + `Cache-Control: max-age=3600`
- 内部ログで `feature_asof` / `feature_current` の使い分けが見える

### E401 webgis: tileLayer URL builder に asOf

受け入れ条件:
- `webgis/src/controllers/layer.ts`: `setBaseLayerSource(ctx, layerId, theme, asOf?)` で URL に `?asOf=` 追加
- `webgis/src/controllers/selection.ts`: `getFeaturesAt(..., asOf?)` 引数追加
- `webgis/src/main.ts`: `layer_select.asOf` 受領 → loadFeatures に伝搬
- `webgis/src/api/client.ts`: `getLayers/getLayerStyle/getLayerExtent/getFeaturesAt` に `asOf?` 引数
- OL TileLayer の source.refresh() で asOf 切替時にキャッシュ消去
- `tsc --noEmit` 0 errors

### E402 windos-app: MainForm asOfPicker + ApiClient asOf 引数 + AttributeEditor disable

受け入れ条件:
- `MainForm.cs`: ツールバーに `DateTimePicker asOfPicker` 追加 (チェックボックス付き、未チェックで null = 現在)
- `asOfPicker.ValueChanged` で `apiClient.GetLayersAsync(asOf)` 再ロード + bridge `layer_select` envelope に `asOf` 載せて再送
- `asOf != null` で saveButton.Enabled=false / deleteButton 等 disable
- `ApiClient.cs`: `GetLayersAsync/GetLayerStyleAsync/GetLayerExtentAsync/GetFeaturesAt` 等 4-5 メソッドに `DateOnly? asOf` 引数追加
- `AttributeEditorControl.cs`: `LoadFeature(schema, feature, asOf?)` で asOf 保持
- `dotnet build -c Release` 成功

### E501 api.tests 新規 5 件

`Tests/Bitemporal/LayerAsOfTests.cs` + `Tests/Admin/StyleHistoryTests.cs` + `Tests/Tiles/TilesAsOfTests.cs` + `Tests/Shared/AsOfParserTests.cs` + `Tests/Bitemporal/LayerUpdateBitemporalTests.cs`。

受け入れ条件:
- `LayerAsOfTests`: 2 layer 作成 + 1 削除 + asOf 過去/現在で結果が異なる
- `StyleHistoryTests`: PUT × 3 → style_version=1/2/3 → asOf で過去 SLD
- `TilesAsOfTests`: asOf あり → no-store ヘッダ + feature_asof featureType を WireMock で確認
- `AsOfParserTests`: ISO datetime 422 / 不正フォーマット 422 / 有効 DateOnly OK
- `LayerUpdateBitemporalTests`: PATCH × 2 で version=1→2、layer_history に 1 行
- 全テスト green、既存 64 + 新規 5 = 69 件

### E502 webgis vitest 新規 2 件

`webgis/test/map/tileLayerAsOf.test.ts` + `webgis/test/controllers/selectionAsOf.test.ts`。

受け入れ条件:
- `tileLayerAsOf.test.ts`: URL に `?asOf=` が含まれる + OL source.refresh() 呼ばれる
- `selectionAsOf.test.ts`: `getFeaturesAt` に asOf 伝搬
- 既存 9 + 新規 2 = 11 件 pass

### E503 windos-app.tests 新規 1 件

`Tests/Forms/MainFormAsOfPickerTests.cs`。

受け入れ条件:
- DateTimePicker.ValueChanged → bridge `layer_select` envelope に asOf 載る
- asOf != null で saveButton.Enabled=false
- 既存 118 + 新規 1 = 119 件 pass (Release 構成)

### E504 e2e smoke + docs/bitemporal-asof.md + PHASE_E_INDEX.md 最終化

受け入れ条件:
- `tools/perf/phase-e-e2e/run.ps1` でシナリオ pass:
  - 1. layer 'test_2025' 作成 (2025-01-01 backdated insert)
  - 2. style PUT × 3 (2025-01-15, 2025-02-15, 2025-03-15)
  - 3. layer DELETE (2025-03-01)
  - 4. `GET /api/layers?asOf=2025-01-10` → test_2025 含む
  - 5. `GET /api/layers?asOf=2025-04-15` → 含まない
  - 6. `GET /tiles/.../?asOf=2025-02-20` → 200 + no-store
- `docs/bitemporal-asof.md` 新規 (300-400 行): asOf 全経路解説 + SLD 履歴例 + Phase A C1 修復との関係
- `docs/PHASE_E_INDEX.md` 最終化 (PR 一覧 + 受け入れ条件チェックリスト + 完了報告)
- `MEMORY.md` を Phase E 完了状態に更新 (orchestration_state.md 連動)
