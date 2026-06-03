# Phase E Index — バイテンポラル全面化 (layers + style_json + asOf 共通展開)

agri-gis Phase E (`バイテンポラル全面化`) サイクルの高位サマリ。Phase A (認証基盤) / Phase B (レイヤ管理 + GeoJSON/CSV) / Phase C (Shapefile + GDAL) / Phase D (描画アーキ転換) 完了後の次サイクル。

## スコープ

Phase A `feature_current/feature_history` で確立した「半開区間 + append-only + audit_log + `CURRENT_DATE` 接合」イディオムを、**`layers` (→ `layer_history`)** と **`style_json` (→ `layer_style_version`)** に横展開する。

汎用 GIS の差別化要素である「過去時点復元 (asOf クエリ)」を、feature だけでなくレイヤ自体と theme/SLD まで対応させる。

## 採用方針

| 観点 | 採用 |
|------|------|
| layer 履歴 | **L-1: `layer_history` 新設** (Phase A `feature_history` 双子) |
| style 履歴 | **S-1: `layer_style_version` 新設** (Phase A `layer_schema_version` 同型) |
| asOf 統一 | `?asOf=YYYY-MM-DD` を 6 endpoint + タイル URL に展開 |
| asOf 粒度 | DATE (feature と統一) |
| `fn_layer_update` 配線 | WE1 で関数 + WE2 で API |
| `deleted_at` 列の DROP | Phase E' 送り (Phase E では二重書き) |
| asOf タイル | `Cache-Control: no-store` |
| GeoServer | `feature_asof` SQL VIEW = `feature_current UNION ALL feature_history` を featureType 化、CQL_FILTER に `valid_from <= asOf < valid_to` 追記 |
| `feature_asof` view 性能 | WE0 PoC で 50 万件 × z=15 < 500ms 確認 (no-go なら Phase E' でパーティショニング) |

詳細は `docs/issues/PHASE_E_DESIGN_P.md`。

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|------|--------|------|------|
| **WE0** | PoC + Design 8 本 | 1.5d | E100 |
| **WE1** | DB 土台 (migration 7 本 + 関数 3 本) | 2.0d | E101-E106 |
| **WE2** | API (AsOfParser + 6 endpoint asOf + PATCH/PUT/DELETE 関数化) | 2.0d | E201-E205 |
| **WE3** | GeoServer (feature_asof featureType + setup.ps1 拡張) | 1.0d | E301-E302 |
| **WE4** | UI (WinForms asOfPicker + WebGIS asOf 配線) | 1.5d | E401-E402 |
| **WE5** | テスト + Docs | 2.0d | E501-E504 |
| | **合計** | **約 10.0d** | **20 Issue** |

クリティカルパス約 7.5 営業日 + バッファ。Phase D (11.5d) より軽い (Phase A C1 修復の知見直接転用)。

詳細は `docs/issues/PHASE_E_WAVE_PLAN.md`。

## 主要 API 変更

| Method | Path | 状態 |
|--------|------|------|
| GET | `/api/layers?asOf=YYYY-MM-DD` | asOf 対応 (E201) |
| GET | `/api/admin/layers?asOf=` | asOf 対応 (E202) |
| GET | `/api/admin/layers/{id}/style?asOf=` | asOf 対応 (E203) |
| GET | `/api/layers/{id}/extent?asOf=` | asOf 対応 (E204) |
| GET | `/api/layers/{id}/at?asOf=` | asOf 対応 (E204) |
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png?asOf=` | feature_asof + no-store (E205, E301-E302) |
| PUT | `/api/admin/layers/{id}/style` | `fn_layer_style_upsert` 経由化 (E203) |
| PATCH | `/api/admin/layers/{id}` | `fn_layer_update` 経由化 (E202) |
| DELETE | `/api/admin/layers/{id}` | `fn_layer_delete` v2 経由化 (E202) |

## DB マイグレーション

| # | ファイル | 内容 |
|---|---------|------|
| 0E01 | `0E01_layer_history.sql` | `layer_history` テーブル新設 |
| 0E02 | `0E02_layers_valid_from_to.sql` | `layers` 列追加 + backfill |
| 0E03 | `0E03_layer_style_version.sql` | `layer_style_version` 新設 |
| 0E04 | `0E04_fn_layer_update.sql` | PATCH 用関数 |
| 0E05 | `0E05_fn_layer_delete_v2.sql` | DELETE 関数 v2 (二重書き) |
| 0E06 | `0E06_fn_layer_style_upsert.sql` | PUT 関数 |
| 0E07 | `0E07_feature_asof_view.sql` | `feature_asof` VIEW |

down script 7 本も同時作成。

## 受け入れ条件 (Phase E 完了の定義)

1. `docker compose up -d` + migration 7 本適用成功
2. `GET /api/layers?asOf=YYYY-MM-DD` が現在と過去で異なる結果
3. `GET /tiles/.../?asOf=YYYY-MM-DD` 200 + `Cache-Control: no-store` + `feature_asof` featureType 経由 PNG
4. WinForms `DateTimePicker` で asOf 設定 → 編集ボタン disable + bridge envelope に asOf
5. WebGIS の TileLayer URL に `?asOf=` 含まれる
6. `api.tests` 全 green (推定 69 件)
7. `webgis vitest` 全 green (推定 11 件)
8. `windos-app.tests` 全 green (推定 119 件)
9. `docs/bitemporal-asof.md` + `docs/rendering.md` への Phase E 章追記
10. PR 単位で全 6 Wave が main にマージ済
11. `orchestration_state.md` メモリ更新

## PR 一覧 (Phase E 完了時に最終化)

| Wave | PR | 状態 |
|------|----|------|
| Design 8 本 + WE0 PoC | (本 PR 候補) | レビュー待ち |
| WE1 (`feature/phase-e-we1-db`) | #??? | 未着手 |
| WE2 (`feature/phase-e-we2-api`) | #??? | 未着手 |
| WE3 (`feature/phase-e-we3-geoserver`) | #??? | 未着手 |
| WE4 (`feature/phase-e-we4-ui`) | #??? | 未着手 |
| WE5 (`feature/phase-e-we5-tests-docs`) | #??? | 未着手 |

## Phase E' 申し送り

- `layers.deleted_at` 列の DROP (Phase E 中の参照 SQL を `valid_to='9999-12-31'` に置換 → DROP COLUMN)
- `layer_history` パーティショニング (年単位 partition or PostgreSQL 16 declarative partitioning) — テーブルサイズが 1000 万行 級に達したら
- WMS GetFeatureInfo 経路の精緻化 (Phase D MVP 未実装、Phase E で `feature_asof` view を使うなら整合性確保)
- 本番 GeoServer の setup.ps1 自動化 (`docs/deploy/geoserver-prod.md` の手動手順を script 化)
- カスタム theme 編集 Web UI (Phase D' 課題、Phase E で土台が整うので着手可能)
- `POST /api/features/batch-update` 一括属性編集 (Phase D' 課題)
- MapProxy 永続キャッシュ層 (Phase D' 課題)

## 関連ドキュメント

- `PHASE_A_INDEX.md` (Phase A 完了)
- `PHASE_B_INDEX.md` (Phase B 完了)
- `PHASE_C_INDEX.md` (Phase C 完了)
- `PHASE_D_INDEX.md` (Phase D 完了)
- `docs/issues/PHASE_E_PLAN.md`
- `docs/issues/PHASE_E_DESIGN_A.md` (採用案)
- `docs/issues/PHASE_E_DESIGN_B.md` (落選: single-table temporal)
- `docs/issues/PHASE_E_DESIGN_C.md` (落選: PostgreSQL temporal_tables)
- `docs/issues/PHASE_E_DESIGN_P.md` (採用案 Picked)
- `docs/issues/PHASE_E_WAVE_PLAN.md`
- `docs/issues/PHASE_E_ISSUES_INDEX.md`

## 関連メモリ

- `bitemporal_audit.md` — Phase A C1/C2 修復の参照実装
- `architecture.md` — ハイブリッド構成
- `orchestration_state.md` — 進捗
- `stacked_pr_pitfall.md` — `base=main` 固定
- `smart_app_control_pitfall.md` — WinForms Release 構成
- `rendering_architecture_shift.md` — Phase D 経路 (Phase E の前提)
