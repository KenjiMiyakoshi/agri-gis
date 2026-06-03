# Phase D' Index — テーマ編集 UI + 即時反映 + 一括編集

agri-gis Phase D' (`カスタム theme UI + cache busting + 一括更新 + イベント通知`) サイクルの高位サマリ。Phase D (描画アーキ転換 = GeoServer 同梱) + Phase E (バイテンポラル全面化) 完了後の次サイクル。

## スコープ

Phase D で確立した「GeoServer サーバラスタタイル + SLD theme」、Phase E で確立した「layer_style_version 履歴管理 + asOf 統一」の上に、**運用 UX を整える層**を追加する。

具体的には:

- **SLD 変更が即時反映されない** (`Cache-Control: max-age=3600` で WebView2 キャッシュ命中) → **タイル URL に style version を載せる cache busting**
- 管理者が SLD を**コードで直接編集する以外の手段がない** → **Monaco エディタ + ライブプレビューの admin 画面**
- 数値属性 (収穫量等) を**手動配色する以外の手段がない** → **カラーランプ UI** (Quantile / EqualInterval / Manual breaks)
- **属性編集が 1 件単位のみ** → **`POST /api/features:batch` 一括更新 API**
- **編集後にユーザーが手動 reload しないと反映されない** → **PostgreSQL LISTEN/NOTIFY + SSE で自動無効化**

「個別 fix の集合」に見えるが、5 件すべて **layer_style_version の `style_version` を URL に伝搬する** という 1 本のレールで統一的に解決する。

## 採用方針

| 観点 | 採用 |
|------|------|
| Cache busting 案 | **案 A: タイル URL に `?sv={styleVersion}` 付与** (case B `no-cache + ETag` は GeoServer 再ラスタライズ毎回で重い、案 C `cache: no-store` は max-age=3600 設計と矛盾) |
| `Cache-Control` 強化 | URL に sv を載せた前提で `max-age=86400, immutable` (asOf 指定時は `no-store` 維持) |
| Theme 編集 UI | Monaco editor を CDN 経由で `admin-style.html` に分離 (一般 WebGIS bundle に影響させない) |
| Theme プレビュー | 同画面右ペインに専用 OL map、PUT → styleVersion +1 → タイル URL 自動更新 |
| カラーランプ算出 | PostgreSQL `ntile()` でサーバ計算、`GET /api/admin/layers/{id}/attributes/{field}/stats?bins=N` |
| Batch update 楽観ロック | **all-or-nothing** (1 件 version mismatch で全件 rollback、Phase D `/at` と同流儀) |
| イベント通知 | **PostgreSQL `LISTEN/NOTIFY` + Server-Sent Events (SSE)** (WebSocket 不要、HTTP/1.1 で完結) |
| Notify トリガ | 既存 7 関数 (`fn_feature_insert/update/delete/fn_layer_style_upsert/fn_layer_update/fn_layer_delete_v2/fn_layer_schema_upsert`) に `pg_notify('agri_gis_layer_invalidate', ...)` 追加 |
| WMS GetFeatureInfo 統合 | **Phase D'' 送り** (E' の高度なクエリと一緒に扱う方が筋) |
| MapProxy / k8s helm | **Phase D''/H 送り** (本番 QPS 観測後 / 本番運用フェーズ) |

詳細は `docs/issues/PHASE_D_PRIME_PLAN.md`。

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|------|--------|------|------|
| **WD'0** | Plan + Design 4 本 | 0.5d | D'100 |
| **WD'1** | DB + API 基盤 (styleVersion + batch + stats) | 1.5d | D'101-D'105 |
| **WD'2** | WebGIS 管理 UI (Monaco エディタ + カラーランプ) | 2.0d | D'201-D'206 |
| **WD'3** | リアルタイム反映 (SSE + batch UI) | 1.5d | D'301-D'305 |
| **WD'4** | テスト + Docs | 1.0d | D'401-D'405 |
| | **合計** | **約 6.5d** | **22 Issue** |

クリティカルパス約 6 営業日 + バッファ。Phase E (10.0d) より軽い (新規 DB 構造ほぼなし、Phase E の `layer_style_version` を活用)。

詳細は `docs/issues/PHASE_D_PRIME_WAVE_PLAN.md`。

## 主要 API 変更

| Method | Path | 状態 |
|--------|------|------|
| GET | `/api/layers` | レスポンスに `styleVersion` フィールド追加 (D'101) |
| GET | `/api/admin/layers` | 同上 (D'101) |
| GET | `/api/admin/layers/{id}/attributes/{field}/stats?bins=N&method=quantile\|equal` | 新設 (D'105) |
| POST | `/api/features:batch` | 新設 (D'104, all-or-nothing) |
| GET | `/api/events/layers/{layerId}/stream` | 新設 (D'301, SSE) |
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png?sv=N` | `sv` クエリパラメータを受領 (D'201, sv は cache key にのみ使う、API ロジック不問) |

## DB マイグレーション

| # | ファイル | 内容 |
|---|---------|------|
| 0F01 | `0F01_fn_feature_batch_update.sql` | 一括属性更新関数 |
| 0F02 | `0F02_notify_invalidation.sql` | 既存 7 関数に `pg_notify` 追加 |

down script 2 本も同時作成。

## 受け入れ条件 (Phase D' 完了の定義)

1. `docker compose up -d` + migration 2 本適用成功
2. SLD を `PUT /api/admin/layers/{id}/style` で更新 → タイル URL に新 `?sv=N+1` が反映 → **WebView2 手動キャッシュクリアなしで** 新 SLD のタイル表示
3. `/admin-style.html` で Monaco エディタによる SLD 編集 + ライブプレビュー動作
4. カラーランプ UI で数値属性を 5 階級 Viridis 配色 → SLD 自動生成 → 適用 → 階級色表示
5. `POST /api/features:batch` で 10 件まとめ更新成功 + 楽観ロック失敗で全件 rollback
6. WinForms で属性編集 → 1 秒以内に WebGIS の地図上で**自動的に**色更新 (SSE 経由)
7. `api.tests` 全 green (推定 95+ 件)
8. `webgis vitest` 全 green (推定 20+ 件)
9. `windos-app.tests` 全 green (推定 122+ 件)
10. `docs/sld-cache-busting.md`, `docs/admin-style-editor.md`, `docs/feature-batch-update.md`, `docs/feature-events-sse.md` の 4 本を作成済
11. PR 単位で全 5 Wave が main にマージ済
12. `orchestration_state.md` メモリ更新

## Phase D'' 申し送り

- **WMS GetFeatureInfo 統合** (現状の `/api/layers/{id}/at` を GeoServer 経由に置き換え or 補強)
- **MapProxy 中間キャッシュ** (本番 QPS が GeoServer 単体の限界を超えてから)
- **SldXmlBuilder 拡張** (TextSymbolizer / RasterSymbolizer / 複雑シンボル)
- **SSE のスケール** (複数 API インスタンスで Redis pub-sub 切替)
- **本番 GeoServer の helm chart** (Phase H6 候補)

## 関連ドキュメント

- `PHASE_A_INDEX.md` (Phase A 完了)
- `PHASE_B_INDEX.md` (Phase B 完了)
- `PHASE_C_INDEX.md` (Phase C 完了)
- `PHASE_D_INDEX.md` (Phase D 完了)
- `PHASE_E_INDEX.md` (Phase E 完了)
- `docs/issues/PHASE_D_PRIME_PLAN.md`
- `docs/issues/PHASE_D_PRIME_WAVE_PLAN.md`
- `docs/issues/PHASE_D_PRIME_ISSUES_INDEX.md`
- `docs/sld-cache-busting.md` (D'1 Design)
- `docs/admin-style-editor.md` (D'2 + D'3 Design)
- `docs/feature-batch-update.md` (D'4 Design)
- `docs/feature-events-sse.md` (D'5 Design)

## 関連メモリ

- `sld_cache_busting.md` — Phase D' 起点となった残課題
- `rendering_architecture_shift.md` — Phase D 経路
- `bitemporal_audit.md` — Phase A/E イディオム
- `orchestration_state.md` — 進捗
- `stacked_pr_pitfall.md` — `base=main` 固定
- `smart_app_control_pitfall.md` — WinForms Release 構成
