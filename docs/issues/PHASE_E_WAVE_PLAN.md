# agri-gis Phase E Wave 分割計画 (案 P)

`PHASE_E_ISSUES_INDEX.md` の 17 Issue を、Phase A WA1〜WA5 / Phase B WB0〜WB5 / Phase C WC0〜WC4 / Phase D WD0〜WD5 と同じ流儀で Wave 分割。**6 Wave 構成 (WE0〜WE5)**、約 10.0 人日。

## 0. 運用前提 (Phase A/B/C/D 踏襲)

- **branch-per-Wave + base=main 固定**: 各 Wave で `feature/phase-e-we{N}-{slug}` ブランチ
- **stacked PR pitfall 回避** (MEMORY.md): Wave PR は常に `base=main`
- **Wave 内 Issue 順**: Issue 番号順を推奨着手順
- **Wave 内コミット**: Issue ID prefix (`E101:`, `E201:` 等)
- **Wave 完了 = main マージ + 検証手順クリア**
- **ラベル**: `wave:WE0`〜`wave:WE5`、`phase:E`、`area:db|api|webgis|winforms|tests|docs|infra` を併用
- **smart_app_control_pitfall**: WinForms ローカル smoke は Release 構成必須 (Phase E でも継続)

## 1. Wave 一覧

| Wave | テーマ | 含む Issue | 工数 | 前提依存 | 並列実行可否 |
|------|--------|-----------|------|---------|-------------|
| **WE0** | PoC スパイク (`feature_asof` view 性能 Gate) + Design 8 本 | E100 | 0.5d + PoC 1.0d = 1.5d | なし | 単独 (Gate) |
| **WE1** | DB 土台 (migration 7 本 + 関数 3 本) | E101, E102, E103, E104, E105, E106 | 2.0d | WE0 go | E101-E103 並列 → E104-E106 並列 |
| **WE2** | API (AsOfParser + 6 endpoint asOf 配線 + PATCH/PUT/DELETE 関数化) | E201, E202, E203, E204, E205 | 2.0d | WE1 完了 | E201 → (E202/E203/E204/E205 並列) |
| **WE3** | GeoServer (feature_asof featureType + setup.ps1 拡張 + TilesEndpoints asOf 分岐) | E301, E302 | 1.0d | WE2 (TilesEndpoints) | E301 → E302 直列 |
| **WE4** | UI (WinForms asOfPicker + WebGIS asOf 配線) | E401, E402 | 1.5d | WE3 完了 (動作確認後) | E401 (WebGIS) と E402 (WinForms) 並列可 |
| **WE5** | テスト + Docs | E501, E502, E503, E504 | 2.0d | WE4 完了 | E501-E503 並列 → E504 |
| | **合計** | **17 Issue** | **約 10.0d** | | |

クリティカルパス: WE0 (1.5d) → WE1 (1.5d 直列分) → WE2 (1.5d) → WE3 (1.0d) → WE4 (1.0d) → WE5 (1.0d 直列分) ≒ **7.5 営業日 + バッファ**。並列度を最大化すれば 7 営業日。

---

## 2. Wave 詳細

### WE0 — Design 8 本 + `feature_asof` view 性能 PoC (Gate)

- **テーマ**: Plan/Design ドキュメント 8 本作成済を main に取り込み + `feature_asof` view の性能要件 (`50 万件 fixture × z=15 タイル < 500ms`) を PoC で確認
- **含む Issue**: E100 (PoC + Design 文書)
- **工数**: Design 0.5d + PoC 1.0d = 1.5d
- **依存**: なし
- **並列**: 単独 (Gate)
- **ブランチ**: `feature/phase-e-design-docs` (Design) + `feature/phase-e-we0-poc` (PoC)
- **マージ順**: 1 番目
- **検証手順**:
  1. Design ドキュメント 8 本が PR で main にマージ済
  2. `tools/perf/feature-asof-50k/generate.sh` で 50 万件 fixture 生成
  3. `feature_asof` view 経由 z=15 タイル (帯広付近) を 5 回平均 < 500ms (cold) / < 50ms (warm)
  4. `docs/issues/PHASE_E_E100_POC_RESULT.md` に結果記録
  5. go/no-go 判定。**no-go の場合**: パーティショニング戦略 (Phase E' 候補) を WE1 前に確定 or Phase E スコープ縮小

### WE1 — DB 土台 (migration 7 本 + 関数 3 本)

- **テーマ**: `layer_history` + `layers` 列追加 + `layer_style_version` + `feature_asof` view + 関数 3 本 (`fn_layer_update`, `fn_layer_delete` v2, `fn_layer_style_upsert`) の DB 構築
- **含む Issue**: E101 (0E01 layer_history, S(0.3d)), E102 (0E02 layers 列追加 + backfill, S(0.4d)), E103 (0E03 layer_style_version, S(0.3d)), E104 (0E04 fn_layer_update, M(0.7d)), E105 (0E05 fn_layer_delete v2, M(0.5d)), E106 (0E06 fn_layer_style_upsert + 0E07 feature_asof view, M(0.5d))
- **工数**: 2.7d (合計、並列で 2.0d 想定)
- **依存**: WE0 go 判定
- **並列**: E101-E103 (テーブル系) 並列、その後 E104-E106 (関数系) 並列
- **ブランチ**: `feature/phase-e-we1-db`
- **マージ順**: 2 番目
- **検証手順**:
  1. `Get-ChildItem db/migration/0E0*.sql | Sort-Object Name | ForEach-Object { docker exec ... }` で全 migration 適用成功
  2. `docker exec agri_postgis psql -c "\d layer_history"` / `"\d layer_style_version"` で新テーブル確認
  3. `docker exec agri_postgis psql -c "SELECT * FROM feature_asof LIMIT 1"` で view 動作
  4. `fn_layer_update(...)` を psql で手動呼び出し、`layer_history` に 1 行退避 + `audit_log` に 1 行記録
  5. `fn_layer_style_upsert(...)` × 2 で `layer_style_version` に 2 行 (`style_version=1,2`)
  6. 既存 `api.tests` 全 green (DB スキーマ変更が既存テストを破壊しないこと)
  7. `db/migration/down/0E0*_down.sql` 7 本も作成、逆順で実行可能

### WE2 — API (AsOfParser + 6 endpoint asOf 配線 + PATCH/PUT/DELETE 関数化)

- **テーマ**: `AsOfParser` 共有化 + 6 endpoint で `?asOf=` 受領 + admin PATCH/PUT/DELETE を関数経由に変更
- **含む Issue**: E201 (AsOfParser shared + LayerEndpoints asOf, S(0.5d)), E202 (AdminLayersEndpoints asOf + PATCH/DELETE 関数化, M(0.6d)), E203 (AdminLayerStyleEndpoints asOf + PUT 関数化, M(0.5d)), E204 (layer/{id}/extent + at asOf, S(0.3d)), E205 (TilesEndpoints asOf, S(0.3d))
- **工数**: 2.2d (合計、並列で 2.0d 想定)
- **依存**: WE1 完了
- **並列**: E201 → (E202/E203/E204/E205 並列)
- **ブランチ**: `feature/phase-e-we2-api`
- **マージ順**: 3 番目
- **検証手順**:
  1. `dotnet build api -c Release` 成功
  2. `dotnet test api.tests -c Release` 全 green (既存 64 + 新規 0、新規テストは WE5 で本実装)
  3. `GET /api/layers?asOf=2025-01-01` が `layers + layer_history` UNION ALL クエリを発行 (psql `EXPLAIN` で確認)
  4. `PUT /api/admin/layers/{id}/style` で `layer_style_version` に新規 INSERT (style_version+1)
  5. `PATCH /api/admin/layers/{id}` で `layer_history` に旧行退避 + version+1
  6. `DELETE /api/admin/layers/{id}` で `valid_to=CURRENT_DATE` + `deleted_at=now()` の二重書き
  7. `?asOf=2026-01-01T00:00:00Z` (ISO datetime) で 422 (DateOnly のみ受領、Phase A 流儀)

### WE3 — GeoServer (feature_asof featureType + setup.ps1 拡張 + TilesEndpoints asOf 分岐)

- **テーマ**: GeoServer に `feature_asof` featureType 追加 + `setup.ps1` 拡張 + TilesEndpoints の URL 分岐
- **含む Issue**: E301 (setup.ps1 + feature_asof featureType POST, M(0.6d)), E302 (TilesEndpoints の asOf 分岐 + Cache-Control: no-store, S(0.4d))
- **工数**: 1.0d
- **依存**: WE2 (TilesEndpoints が asOf 受領可能になっている)
- **並列**: E301 → E302 直列
- **ブランチ**: `feature/phase-e-we3-geoserver`
- **マージ順**: 4 番目
- **検証手順**:
  1. `tools/geoserver-setup/setup.ps1` 実行で `agrigis:feature_asof` featureType 公開
  2. WMS GetMap (直叩き) で `feature_asof` から PNG 取得成功
  3. `GET /tiles/{id}/default/15/.../?asOf=2025-01-01` 経由で 200 + `Cache-Control: no-store` ヘッダ
  4. `GET /tiles/{id}/default/15/.../` (asOf なし) は `max-age=3600, public` (既存維持)
  5. GeoServer reload API (`POST /rest/reload`) が setup.ps1 内で呼ばれる
  6. 既存 `api.tests` 全 green

### WE4 — UI (WinForms asOfPicker + WebGIS asOf 配線)

- **テーマ**: WinForms に `DateTimePicker` 追加 + 編集ボタン disable + WebGIS の TileLayer URL に asOf
- **含む Issue**: E401 (WebGIS asOf 配線 + tileLayer URL builder, M(0.7d)), E402 (WinForms asOfPicker + ApiClient asOf 引数 + AttributeEditor disable, M(0.8d))
- **工数**: 1.5d
- **依存**: WE3 完了
- **並列**: E401 (WebGIS) と E402 (WinForms) は API シグネチャ合意後並列可
- **ブランチ**: `feature/phase-e-we4-ui`
- **マージ順**: 5 番目
- **検証手順**:
  1. `dotnet build windos-app -c Release` 成功 (SAC 回避)
  2. `pnpm dev` で WebGIS 起動、ブラウザで `?asOf=` 付き TileLayer URL が叩かれる
  3. WinForms 起動 → ログイン → `asOfPicker` 値変更 → bridge envelope に `asOf` 載る (`messages.test.ts` ロギング)
  4. asOf 設定中: saveButton.Enabled=false, deleteButton.Enabled=false, addButton.Enabled=false
  5. asOf 解除 (null): 編集ボタン復活
  6. 既存 `windos-app.tests` 全 green

### WE5 — テスト + Docs

- **テーマ**: 新規テスト + e2e smoke + Docs
- **含む Issue**: E501 (api.tests 新規 5 件, M(0.6d)), E502 (webgis vitest 新規 2 件, S(0.3d)), E503 (windos-app.tests 新規 1 件, S(0.3d)), E504 (e2e smoke + docs/bitemporal-asof.md + PHASE_E_INDEX.md 最終化, M(0.8d))
- **工数**: 2.0d
- **依存**: WE4 完了
- **並列**: E501-E503 並列 → E504
- **ブランチ**: `feature/phase-e-we5-tests-docs`
- **マージ順**: 6 番目
- **検証手順**:
  1. `dotnet test api.tests -c Release` 全 green (Phase D 64 + 新規 5 = 約 69+ 件)
  2. `npm test webgis` 全 green (Phase D 9 + 新規 2 = 11 件)
  3. `dotnet test windos-app.tests -c Release` 全 green (Phase D 118 + 新規 1 = 119 件)
  4. e2e smoke: `tools/perf/phase-e-e2e/run.ps1` で「layer 作成 → style 更新 × 3 → 削除 → 各 asOf で確認」シナリオ pass
  5. `docs/bitemporal-asof.md` (asOf 全経路解説、SLD 履歴例 1 件) 作成
  6. `docs/PHASE_E_INDEX.md` 最終化 (PR 一覧 + 受け入れ条件チェックリスト)
  7. `orchestration_state.md` を Phase E 完了状態に更新

---

## 3. クリティカルパスと並列度

```
WE0 (Gate) ─ E100 ──┬─ WE1 ─ E101 ─┬─ E102 ─┬─ E104 ─┐
                    │              │        ├─ E105 ─┤
                    │              │        └─ E106 ─┤
                    │              └─ E103 ─────────┤
                    └─────────────────────── WE2 ─ E201 ─┬─ E202 ─┐
                                                        ├─ E203 ─┤
                                                        ├─ E204 ─┤
                                                        └─ E205 ─┴─ WE3 ─ E301 ─ E302 ─┐
                                                                                       ├─ WE4 ─ E401, E402 並列 ─┐
                                                                                       │                        │
                                                                                       └────────────────────────┴─ WE5 ─ E501-E503 並列 ─ E504
```

並列実装可能な単位:
- WE1 内: E101 / E102 / E103 (テーブル系) 並列、その後 E104 / E105 / E106 (関数系) 並列
- WE2 内: E201 → (E202/E203/E204/E205 並列)
- WE4 内: E401 (WebGIS) と E402 (WinForms)
- WE5 内: E501/E502/E503 並列 → E504

並列最大化したクリティカルパス: WE0 (1.5d) + WE1 (1.5d) + WE2 (1.5d) + WE3 (1.0d) + WE4 (1.0d) + WE5 (1.0d) ≒ **7.5d**。

実装者 1 人想定なら約 10d、2 人並列なら約 7d。

## 4. 既知の制約と申し送り

- **`feature_asof` view の性能**: WE0 PoC 結果次第。`> 2s` の場合パーティショニング (Q1) 検討 or Phase E' に持ち越し
- **`deleted_at` 二重書き**: Phase E では仕様、Phase E' で参照削除 + DROP COLUMN を起票 (`phase-e-prime-followup` ラベル)
- **CI 上で GeoServer を起動しない方針**: api.tests は GeoServer モック (Phase D で既に確立)
- **テスト時間**: Phase E 新規 8 件 + 既存 64 = 約 72 件で実行時間が 25-30s 想定 (Phase D の 21s から 5-10s 増)
- **本番 GeoServer デプロイは Phase E 外**: setup.ps1 の本番対応は別タスク (Phase D' or Phase E' 候補)

## 5. 関連ドキュメント

- `PHASE_E_PLAN.md`: Plan 工程
- `PHASE_E_DESIGN_P.md`: 採用案
- `PHASE_E_ISSUES_INDEX.md`: Issue 一覧 + 詳細
- `docs/PHASE_E_INDEX.md`: 高位サマリ
