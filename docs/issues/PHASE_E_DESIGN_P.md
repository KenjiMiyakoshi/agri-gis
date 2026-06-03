# Phase E Design 案 P — 採択案 (案 A ベース + Plan ユーザー判断 4 件反映)

`PHASE_E_DESIGN_A.md` (L-1 `layer_history` + S-1 `layer_style_version`) をベースに、`PHASE_E_PLAN.md` §3.2 のユーザー判断 4 件を反映した最終 Design。Phase E Issue 化フェーズの直接入力。

## 1. 採用ベースと選択理由

**ベース案 = 案 A (Phase A 流儀完全踏襲)**

選択理由:
- Phase A C1/C2 修復で確立した「半開区間 + append-only + audit_log + CURRENT_DATE 接合」イディオムを 100% 再利用
- `feature_current/feature_history` と `layers/layer_history` が完全対称、引継ぎ時の認知負荷ゼロ
- 既存 FK 構造を破壊しない (`feature_current.layer_id → layers.layer_id` は引き続き UNIQUE)
- `layer_schema_version` (Phase A 0106) と `layer_style_version` (Phase E 新設) が完全対称
- 案 B (single-table temporal) は FK 全壊 + 既存 SQL 全件監査が必要、案 C (拡張) は kartoza image 同梱問題 + Phase A 流儀放棄

## 2. ユーザー判断 4 件の反映 (Plan §3.2)

### 2.1 valid_from/valid_to の粒度 = DATE

すべての新規列 (`layers.valid_from/_to`, `layer_history.valid_from/_to`, `layer_style_version.valid_from/_to`) を **DATE 型** で揃える。

理由:
- 既存 `feature_current.valid_from/_to` (Phase A) が DATE
- C1 修復ロジック (`valid_to=CURRENT_DATE` + 半開区間ゼロ幅) がそのまま転用可能
- API `?asOf=YYYY-MM-DD` の DateOnly パーサも転用可能

`layer_schema_version` の TIMESTAMPTZ 粒度との不揃いは Phase E では解消しない (`layer_schema_version` 単体での過去検索は元々ほとんど使われていないため放置可)。

### 2.2 `fn_layer_update` 配線 = WE1 で関数 + WE2 で API

PR 単位の責務分割:
- WE1 PR: DB migration (関数定義) のみ。API は触らない
- WE2 PR: API 実装 (`AdminLayersEndpoints` の PATCH 経路を inline SQL → `fn_layer_update` 呼出に置換)

理由:
- DB レビューと API レビューを別 PR にして SQL レビュアーと .NET レビュアーを分けやすくする
- WE1 マージ後 WE2 着手で依存明確化
- PR 単位の commit が小さくなる (revert しやすい)

### 2.3 `layers.deleted_at` 列の最終撤去 = Phase E' 送り

Phase E 中の挙動:
- `fn_layer_delete` v2 で `deleted_at = now()` と `valid_to = CURRENT_DATE` を **両方** 書く
- API の SQL は `WHERE deleted_at IS NULL` を引き続き踏みつつ、新規 asOf 経路は `valid_to = '9999-12-31'` を引く
- 「2 つの真実」が同時に立つが、両者は同じレコードに対して矛盾しない

Phase E' で実施 (Issue 起票して `phase-e-prime-followup` ラベル):
1. 全 API SQL の `WHERE deleted_at IS NULL` を `WHERE valid_to = '9999-12-31'::date` に置換
2. `fn_layer_delete` から `deleted_at = now()` を削除
3. `layers.deleted_at` 列を DROP

理由:
- Phase E 中の二重書きは安全 (どちらも書く順序が確定、テスト容易)
- 列 DROP は全 caller の grep 監査が必要、Phase E のスコープ膨張回避
- Phase E' で集中対応のほうが回帰リスク低い

### 2.4 asOf ありタイル = `Cache-Control: no-store`

`GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png?asOf=...` のレスポンスは:
- HTTP ヘッダ `Cache-Control: no-store`
- API 内部キャッシュも保持しない (HttpClient レスポンスをそのまま pipe)

asOf 無し経路は従来通り `max-age=3600, public` を維持 (タイル cache hit 率温存)。

理由:
- 過去参照は頻度低、cache key (theme, asOf, z, x, y) で組合せ爆発 → 容量肥大化のリスク
- 過去 SLD で生成されたタイルが「現在 SLD」と混ざる UX バグの防止
- Phase D'  で MapProxy 永続キャッシュを導入する際に asOf 経路は除外しやすい (no-store 経路のまま)

## 3. 未確定論点 (Phase E 中に決定)

| # | 論点 | 着手 Wave |
|---|------|----------|
| Q1 | `layer_history` のパーティショニング戦略 (年単位 partition? PostgreSQL 16 declarative partitioning?) | WE5 (性能 smoke で必要性判明後) |
| Q2 | asOf 設定中の admin UI (theme 編集や layer 削除) を全 disable にするか、編集試行で 422 警告にとどめるか | WE4 (UI 実装時) |
| Q3 | `feature_asof` view の SELECT 列順序 (`feature_current` と完全一致させるか、optimization で削除可能列を絞るか) | WE1 (DB 設計時) |
| Q4 | `audit_log.meta_jsonb` に `valid_from`/`valid_to`/`version` をいつ書き込むか (`fn_layer_update`/`fn_layer_delete` で常時 vs オプション) | WE1 |
| Q5 | WinForms 編集ボタン disable 条件 (asOf != null のみ vs asOf != null OR guest role) | WE4 |

各論点は実装時に PR description で仮採用 + ユーザーレビューで確定 (Phase B/C/D 流儀)。

## 4. Phase E で追加する DB マイグレーション (6 本)

| # | ファイル | 内容 |
|---|---------|------|
| 0E01 | `0E01_layer_history.sql` | `layer_history` テーブル新設 (Phase A `feature_history` 同型) |
| 0E02 | `0E02_layers_valid_from_to.sql` | `layers.valid_from/_to/version` 列追加 + 既存 deleted_at IS NOT NULL を valid_to に backfill |
| 0E03 | `0E03_layer_style_version.sql` | `layer_style_version` テーブル新設 (Phase A `layer_schema_version` 同型) |
| 0E04 | `0E04_fn_layer_update.sql` | `fn_layer_update` 新設 (PATCH 用、楽観ロック + history 退避 + audit_log) |
| 0E05 | `0E05_fn_layer_delete_v2.sql` | `fn_layer_delete` v2 化 (旧 `deleted_at` + 新 `valid_to` 二重書き) |
| 0E06 | `0E06_fn_layer_style_upsert.sql` | `fn_layer_style_upsert` 新設 (`fn_layer_schema_upsert` 同型) |
| 0E07 | `0E07_feature_asof_view.sql` | `CREATE OR REPLACE VIEW feature_asof AS SELECT ... FROM feature_current UNION ALL SELECT ... FROM feature_history` |

down script (`db/migration/down/0E0*_down.sql`) も 7 本作成 (逆順で実行可能)。

## 5. Phase E で追加/変更する API endpoint (6 本)

| Method | Path | 変更 |
|--------|------|------|
| GET | `/api/layers?asOf=YYYY-MM-DD` | asOf 対応 (UNION ALL) |
| GET | `/api/admin/layers?asOf=YYYY-MM-DD` | asOf 対応 |
| GET | `/api/admin/layers/{id}/style?asOf=YYYY-MM-DD` | asOf 対応 (`layer_style_version` SELECT) |
| GET | `/api/layers/{id}/schema?asOf=YYYY-MM-DD` | asOf 対応 (`layer_schema_version` 既存テーブル流用) |
| GET | `/api/layers/{id}/extent?asOf=` | `feature_asof` view 経由 |
| GET | `/api/layers/{id}/at?asOf=` | 同上 |
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png?asOf=` | `feature_asof` featureType に切替 + `Cache-Control: no-store` |
| PUT | `/api/admin/layers/{id}/style` | 内部 SQL を `fn_layer_style_upsert` 呼出に変更 |
| PATCH | `/api/admin/layers/{id}` | 内部 SQL を `fn_layer_update` 呼出に変更 |
| DELETE | `/api/admin/layers/{id}` | 内部 SQL を `fn_layer_delete` v2 呼出 (関数自体は既存名 CREATE OR REPLACE) |

## 6. WebGIS 変更 (4 ファイル)

| ファイル | 変更概要 | Issue |
|----------|---------|-------|
| `webgis/src/controllers/layer.ts` | `setBaseLayerSource(ctx, layerId, theme, asOf?)` で URL に `?asOf=` 追加 | E401 |
| `webgis/src/controllers/selection.ts` | `getFeaturesAt(..., asOf?)` 引数追加 | E401 |
| `webgis/src/main.ts` | `layer_select.asOf` 受領 → loadFeatures に伝搬 | E401 |
| `webgis/src/api/client.ts` | `getLayers/getLayerStyle/getLayerExtent/getFeaturesAt` に `asOf?` 引数追加 | E401 |

## 7. WinForms 変更 (3 ファイル)

| ファイル | 変更概要 | Issue |
|----------|---------|-------|
| `windos-app/Forms/MainForm.cs` | ツールバーに `DateTimePicker asOfPicker` 追加。`asOf != null` で編集ボタン disable + bridge `layer_select.asOf` 送出 | E402 |
| `windos-app/Services/ApiClient.cs` | `GetLayersAsync/GetLayerStyleAsync/.../etc 4 メソッドに `DateOnly? asOf` 引数追加 | E402 |
| `windos-app/Controls/AttributeEditorControl.cs` | `LoadFeature(schema, feature, asOf?)` で asOf 保持、`saveButton.Enabled = (asOf == null)` | E402 |

## 8. テスト戦略

### 8.1 新規テスト

| カテゴリ | テスト | Issue |
|----------|-------|-------|
| API | `LayerAsOfTests` (現在 / 過去で layer 一覧変化、Active layer + history union 動作) | E501 |
| API | `StyleHistoryTests` (PUT × 3 で style_version=1/2/3、asOf で過去 SLD) | E501 |
| API | `TilesAsOfTests` (asOf あり → no-store + feature_asof featureType) | E501 |
| API | `AsOfParserTests` (`/Shared/AsOfParser.cs` 単体) | E501 |
| API | `LayerUpdateBitemporalTests` (PATCH × 2 で version=1→2、layer_history に退避) | E501 |
| WinForms | `MainFormAsOfPickerTests` (asOfPicker → bridge + 編集 disable) | E503 |
| WebGIS | `tileLayerAsOf.test.ts` (URL に `?asOf=`) | E502 |
| WebGIS | `selectionAsOf.test.ts` (`getFeaturesAt` に `asOf=` 伝搬) | E502 |

### 8.2 e2e smoke (E504)

```
1. layer 'test_2025' を 2025-01-01 に作成 (POST /api/admin/layers + Manual valid_from=2025-01-01)
2. style を 2025-01-15 / 2025-02-15 / 2025-03-15 で 3 回更新
3. layer を 2025-03-01 に削除 (DELETE)
4. GET /api/layers?asOf=2025-01-10 → test_2025 含む
5. GET /api/layers?asOf=2025-04-15 → test_2025 含まない
6. GET /api/admin/layers/{id}/style?asOf=2025-01-20 → 1 番目の SLD
7. GET /api/admin/layers/{id}/style?asOf=2025-02-20 → 2 番目の SLD
8. GET /tiles/{id}/default/15/.../...png?asOf=2025-02-20 → 200 PNG (Cache-Control: no-store)
9. GET /tiles/{id}/default/15/.../...png?asOf=2025-04-15 → 200 PNG (空 = layer 削除済)
```

## 9. 案 A からの変更点 (Plan ユーザー判断の反映)

| 案 A | 案 P (本案) |
|------|------------|
| valid_from/_to の型を明記せず | DATE 型に確定 (ユーザー判断 1) |
| `fn_layer_update` の Wave 配置を曖昧 | WE1 で関数 + WE2 で API 配線 (ユーザー判断 2) |
| `deleted_at` の最終扱い未確定 | Phase E では二重書き、列 DROP は Phase E' 送り (ユーザー判断 3) |
| asOf キャッシュ未確定 | `Cache-Control: no-store` 確定 (ユーザー判断 4) |
| `feature_asof` view 性能未検証 | WE0 で 50 万件 fixture × z=15 タイル < 500ms PoC (Plan 推奨) |

## 10. リスク (案 P 残存)

| # | リスク | 対応 |
|---|--------|------|
| P1 | `feature_asof` view の UNION ALL 性能 (数百万件規模) | WE0 で 50 万件 fixture × z=15 平均 < 500ms 確認 (PHASE_E_E100_POC_RESULT.md に記録) |
| P2 | 「2 つの真実」状態 (`deleted_at` + `valid_to`) のテスト書き分け | テストで「両方の WHERE 句で同じ結果」を確認するレグレッションテスト 1 本 (E501) |
| P3 | DateOnly 同日多重 PATCH の半開区間ゼロ幅 (Phase A C1 と同じ) | 既存 `AsOfTests` パターン踏襲、新規 `LayerUpdateBitemporalTests` で同日 PATCH × 2 を検証 (E501) |
| P4 | GeoServer `feature_asof` featureType の追加で datastore reload が必要? | setup.ps1 で featureType POST 成功時に GeoServer reload API (`POST /rest/reload`) を打つ (E301) |
| P5 | WebGIS の TileLayer URL 変更で OL のタイルキャッシュが asOf 切替時に消えない | OL の `tileSource.refresh()` を呼ぶ (E401) |
| P6 | layer_history のテーブルサイズ (1 layer × 1000 PATCH = 1000 行 + 10000 layer = 1000 万行) | WE5 性能 smoke で `EXPLAIN ANALYZE`、Phase E' でパーティショニング検討 (Q1) |

## 11. 受け入れ条件 (Phase E 完了の定義)

1. `docker compose up -d` + `Get-ChildItem db/migration/0E0*.sql | ... apply` で全 7 migration 適用成功
2. `GET /api/layers?asOf=YYYY-MM-DD` が現在と過去で異なる結果を返す (e2e smoke 検証)
3. `GET /tiles/.../?asOf=YYYY-MM-DD` が 200 + `Cache-Control: no-store` + `feature_asof` featureType 経由 PNG を返す
4. WinForms `DateTimePicker` で asOf 設定 → 編集ボタン disable + bridge envelope に asOf 載る
5. WebGIS の TileLayer URL に `?asOf=` 含まれる (asOf 設定時)
6. `api.tests` 全 green (Phase D 64 + Phase E 新規 約 20 件 = 約 84 件)
7. `webgis vitest` 全 green (Phase D 9 + Phase E 新規 約 2 件 = 約 11 件)
8. `windos-app.tests` 全 green (Phase D 118 + Phase E 新規 約 1 件 = 約 119 件)
9. `docs/bitemporal-asof.md` + `docs/rendering.md` への Phase E 章追記
10. PR 単位で全 6 Wave (WE0-WE5) が main にマージ済
11. `orchestration_state.md` メモリ更新 (Phase E 完了状態)

## 12. 関連ドキュメント

- `PHASE_E_PLAN.md`: Plan 工程
- `PHASE_E_DESIGN_A.md`: 採用ベース案
- `PHASE_E_DESIGN_B.md`: 落選案 B (single-table temporal)
- `PHASE_E_DESIGN_C.md`: 落選案 C (PostgreSQL temporal_tables)
- `PHASE_E_WAVE_PLAN.md`: Wave 分割詳細
- `PHASE_E_ISSUES_INDEX.md`: Issue 一覧 + 各 Issue 詳細
- `docs/PHASE_E_INDEX.md`: 高位サマリ
