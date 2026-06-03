# agri-gis Phase E Plan — バイテンポラル全面化 (layers + style_json + asOf 共通展開)

Phase A (認証基盤) / Phase B (レイヤ管理 + GeoJSON/CSV) / Phase C (Shapefile + GDAL) / Phase D (描画アーキ転換、サーバラスタタイル) 完了後の次サイクル。Phase D' に進む前段として、汎用 GIS の差別化要素である「過去時点復元 (asOf クエリ)」を **`layers` 自体と `style_json`** に展開する。

## 1. 動機

### 1.1 ユーザ要件 (2026-06-03 確定)

> 汎用 GIS の仕組み自体は過去時点に戻ることも売りです。そこでレイヤーの追加、削除も履歴として管理しなくてはいけないかと思います。レイヤー自体にも有効期限の概念が必要で、属性情報の変更や新規追加、削除なども履歴管理されるべきではないでしょうか。

### 1.2 Phase A/B/C/D 完了時点の現状

| ドメイン | 現状 | バイテンポラル対応 |
|----------|------|-------------------|
| `feature_current` / `feature_history` / `audit_log` | Phase A C1/C2 修復済、`valid_from`/`valid_to` 半開区間 + `CURRENT_DATE` 接合 | **完備** |
| `layer_schema_version` | Phase A 0106、`(layer_id, schema_version)` + `valid_from`/`valid_to` | **完備** (schema 変更は履歴化) |
| **`layers` 本体** | `deleted_at TIMESTAMPTZ NULL` 1 列のみ | **不完備** (改名・組織移管・削除が時系列に並ばない) |
| **`layers.style_json`** (Phase D D102) | JSONB 直接 UPDATE で上書き | **不完備** (theme 変更追跡不可、過去時点の SLD で再描画不可) |
| API asOf | `GET /api/features/{id}?asOf=YYYY-MM-DD` のみ | **不完備** (layers / style / tile に未配線) |
| GeoServer | `feature_current` 直接 publish + `CQL_FILTER=layer_id=N` | **不完備** (history union view 未提供) |
| WinForms / WebGIS | bridge envelope に `asOf?` フィールド定義済だが API 未配線 | **不完備** (asOf UI なし) |

### 1.3 ボトルネック (Phase A/B/C/D の延長で実装すると詰む)

- Phase D' で「カスタム theme 編集 Web UI」を作っても、style_json の履歴がない → 「2025-01-01 の theme でタイルを再生」が不可能
- Phase D' の `POST /api/features/batch-update` 一括属性編集も、layer 自体が履歴化していないと「2025-04-01 時点で layer 7 が存在したか」を判定できない
- 「過去時点復元」を売りにする汎用 GIS としての差別化が不完全のまま固まる

## 2. 採用方針 (1 行)

**Phase A `feature_current/feature_history` で確立した「半開区間 + append-only + audit_log + CURRENT_DATE 接合」イディオムを、`layers` (→ `layer_history`) と `style_json` (→ `layer_style_version`) に横展開する。**

## 3. Plan 工程の経過

### 3.1 Plan エージェント出力 (2026-06-03)

Plan エージェントが 3 軸 (layer 履歴 / style 履歴 / asOf 統一) について比較案を出した。

**L 軸: layer 履歴管理**

| 案 | 仕組み | 採否 |
|----|--------|------|
| **L-1: `layer_history` 新設** (Phase A 流儀踏襲) | `layers` + `layer_history`、`fn_layer_update/delete` で旧行を history へ退避 | **採用** |
| L-2: `layers` 単独に `valid_from/valid_to` (single-table temporal) | PK を `(layer_id, valid_from)` に変更 | 落選 (FK 全壊、既存 5 migration + 4 FK の張り替え) |
| L-3: PostgreSQL temporal_tables 拡張 | DDL 1 文で済むが拡張同梱不要 | 落選 (kartoza/postgis に同梱なし、Phase A の自前制御一貫性を崩す) |

**S 軸: style_json 履歴管理**

| 案 | 仕組み | 採否 |
|----|--------|------|
| **S-1: `layer_style_version` 新設** (`layer_schema_version` の完全コピー) | `(layer_id, style_version, style_json, valid_from, valid_to, created_by)` | **採用** |
| S-2: `audit_log.after_doc` から style 履歴を逆引き | テーブル増えない | 落選 (audit_log は監査用、データ取得 API に流さない原則違反) |
| S-3: `layers.style_json` に履歴を内包 (`{"themes": {...}, "history": [...]}`) | DDL 不要 | 落選 (JSONB モノリス化、同時更新競合制御困難) |

**T 軸: asOf 統一**

- 既存 `GET /api/features/{id}?asOf=YYYY-MM-DD` の流儀踏襲 (DateOnly、ISO datetime は 422)
- 6 endpoint に展開: layers / admin layers / admin style / layer schema / layer extent / layer at / tiles

### 3.2 ユーザー判断 (2026-06-03)

Plan エージェント提示の 5 論点を確定:

| 論点 | 決定 | Phase E での影響 |
|------|------|------------------|
| `valid_from/valid_to` の粒度 | **DATE** (feature と統一) | C1 修復の半開区間ロジック流用、asOf 粒度を 1 規約に |
| `fn_layer_update` 関数化のタイミング | **WE1 で関数 + WE2 で API 配線** | PR 単位の責務分割、SQL レビューしやすい |
| `layers.deleted_at` 列の最終撤去 | **Phase E' 送り** | Phase E では二重書き、回帰リスク回避 |
| asOf ありタイルのキャッシュ | **`Cache-Control: no-store`** | 過去参照は頻度低、cache key (theme,asOf,z,x,y) で肥大化防止 |
| `feature_asof` view の性能検証 | **WE0 でミニ PoC** | 50 万件 fixture × z=15 タイル < 500ms 確認 (Phase D 末で性能 smoke が遅すぎた反省) |

## 4. スコープ (Phase E で対応する範囲)

### 4.1 含む

- DB migration: `layer_history` + `layer_style_version` + `feature_asof` VIEW + 関数 4 本 (`fn_layer_update`, `fn_layer_delete v2`, `fn_layer_style_upsert`, 既存 `fn_layer_create` は変更不要)
- API: `AsOfParser` 共通化 + 6 endpoint に `?asOf=` 展開
- GeoServer: `feature_asof` を featureType 化、`setup.ps1` 拡張、TilesEndpoints の URL 分岐
- WinForms: MainForm に `DateTimePicker` 追加、asOf 設定中は編集ボタン非活性
- WebGIS: TileLayer URL builder に asOf、bridge `layer_select.asOf` 配線
- テスト: `LayerAsOfTests`, `StyleHistoryTests`, `TilesAsOfTests`, e2e (「2025-01-01 時点での layer 一覧」「同 theme でタイル PNG」)
- Docs: `docs/bitemporal-asof.md` (asOf 全体経路解説)

### 4.2 含まない (Phase E' 以降の申し送り)

- `layers.deleted_at` 列の DROP (二重書き継続、Phase E' で参照削除 + DROP)
- カスタム theme 編集 Web UI (Phase D' 課題)
- `POST /api/features/batch-update` 一括属性編集 (Phase D' 課題、Phase E 後に着手しやすくなる)
- MapProxy 永続キャッシュ層 (Phase D' 課題)
- `audit_log.actor_user_id` の null 行 backfill (該当なし、Phase A で既に NOT NULL)

## 5. 既存資産との整合

| 観点 | 状態 |
|------|------|
| Phase A `fn_feature_*` バイテンポラル接合 | 完全維持 (C1 修復ロジックは Phase E で再利用) |
| Phase B `fn_layer_create/delete` | `fn_layer_create` は無変更、`fn_layer_delete` は v2 化 (履歴退避追加) |
| Phase D D203 `IGeoServerStyleSync` | `fn_layer_style_upsert` 経由に書き換え (PUT 時 DB 履歴 append → GeoServer 同期 → Tx 内) |
| `layer_import_job` (Phase B B104) | 触らない (layer_id を引くだけで履歴非対象) |
| `audit_log` (Phase A C2 修復) | `fn_layer_update/delete/style_upsert` でも 1 件 1 行で記録 |
| WebGIS Phase D `tileLayer` URL builder | `?asOf=` を query string 追加 |

## 6. リスクと申し送り

| # | リスク | 緩和策 |
|---|--------|--------|
| R1 | `deleted_at` 列の二重書き状態が永久化する誘惑 | Phase E' で参照削除 + DROP を Issue 化 (`phase-e-prime-followup` ラベル) |
| R2 | `feature_asof` view の性能 (UNION ALL on million rows) | WE0 で 50 万件 fixture × z=15 タイル < 500ms PoC で go/no-go 判定 |
| R3 | asOf 配線テストの工数 (6 endpoint × 多パターン) | `AsOfParser` 共通化 + テストヘルパで圧縮 |
| R4 | GeoServer datastore キャッシュ問題 (featureType 追加で再起動必要?) | `setup.ps1` を idempotent に保ち、必要なら GeoServer reload API を打つ |
| R5 | WinForms 編集ボタン非活性条件の漏れ | テストで「asOf 設定 → 編集ボタン disable」を 1 件追加 |
| R6 | DateOnly 粒度の同日多重更新 | C1 修復で確立済の「ゼロ幅区間 [today, today)」挙動を踏襲 (Phase A AsOfTests と整合) |

## 7. 工数概算

| Wave | 工数 |
|------|------|
| WE0 (Plan/Design 完了 + 性能 PoC) | 0.5d + PoC 1d = 1.5d |
| WE1 (DB 土台) | 2.0d |
| WE2 (API) | 2.0d |
| WE3 (GeoServer) | 1.0d |
| WE4 (UI) | 1.5d |
| WE5 (テスト + Docs) | 2.0d |
| **合計** | **約 10.0d** |

Phase D (11.5d) より軽い。Phase A C1 修復の知見直接転用 + Phase D で GeoServer 経路確立済のため。

## 8. 次工程

| 工程 | 出力 |
|------|------|
| Design A/B/C 並列展開 | `PHASE_E_DESIGN_A/B/C.md` |
| Design 採用案 (Picked) | `PHASE_E_DESIGN_P.md` |
| Wave 分割 | `PHASE_E_WAVE_PLAN.md` |
| Issue 一覧 + 詳細 | `PHASE_E_ISSUES_INDEX.md` |
| 高位サマリ | `docs/PHASE_E_INDEX.md` |
| **GH Issue 起票** | label `phase:E` / `wave:WE0-WE5` / `area:db|api|webgis|winforms|tests|docs|infra` |
| WE0 PoC 着手 | `tools/perf/feature-asof-50k/` + go/no-go 判定 → `PHASE_E_E100_POC_RESULT.md` |

## 9. 関連メモリ

- `bitemporal_audit.md` — Phase A C1/C2 修復の参照実装
- `architecture.md` — WinForms+WebView2+WebGIS+API+PostGIS ハイブリッド構成
- `orchestration_state.md` — Phase A/B/C/D 完了状態 + Phase E 着手ポイント
- `stacked_pr_pitfall.md` — `base=main` 固定ルール (Phase E も踏襲)
- `smart_app_control_pitfall.md` — WinForms ローカル smoke は Release 構成必須
- `rendering_architecture_shift.md` — Phase D で確立した TileLayer + GeoServer 経路 (Phase E の前提)
