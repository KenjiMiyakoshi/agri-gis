# Phase D' Issues Index

Phase D' で起票する全 22 Issue の一覧。GitHub Issue 起票時のテンプレートにもなる。

ラベル: `phase:D-prime`, `wave:WD'N`, `area:db|api|webgis|winforms|tests|docs`

## WD'0 — Plan + Design

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| D'100 | Phase D' Plan + Design 4 本作成 (`PHASE_D_PRIME_INDEX.md` + Plan/Wave/Issues + cache-busting/admin-style-editor/feature-batch-update/feature-events-sse) | docs | 0.5d |

## WD'1 — DB + API 基盤

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| D'101 | `GET /api/layers` レスポンスに `styleVersion` 追加 (LayerDto + AdminLayerDto + LayersEndpoints + AdminLayersEndpoints) | api | 0.2d |
| D'102 | `TilesEndpoints.TileFileResult` の `Cache-Control` を `max-age=86400, immutable` に強化 (asOf 時 `no-store` 維持) | api | 0.2d |
| D'103 | `0F01_fn_feature_batch_update.sql`: 一括更新関数 (all-or-nothing, 楽観ロック, audit_log) | db | 0.5d |
| D'104 | `POST /api/features:batch` 新設 + `FeatureBatchUpdateRequestDto/ResponseDto/Result` | api | 0.4d |
| D'105 | `GET /api/admin/layers/{id}/attributes/{field}/stats?bins=N&method=quantile\|equal` 新設 (PostgreSQL `ntile()`) | api | 0.2d |

## WD'2 — WebGIS 管理 UI

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| D'201 | `setBaseLayerSource` URL に `?sv=${styleVersion}` 追加 + `LayerDto.styleVersion` 伝搬 + `loadFeatures` シグネチャ拡張 | webgis | 0.2d |
| D'202 | Admin theme editor `webgis/src/admin/styleEditor.ts` + `admin-style.html` エントリ (Monaco loader CDN) | webgis | 0.5d |
| D'203 | Style editor ライブプレビュー (OL map 右ペイン + debounce 500ms PUT + style_version 反映後の再描画) | webgis | 0.4d |
| D'204 | カラーランプ UI `webgis/src/admin/colorRamp.ts` (属性 + bins + 色パレット + ヒストグラム svg) | webgis | 0.5d |
| D'205 | `SldXmlBuilder.cs` 拡張: `colorRamp` JSON 受領 → N 段 Rule 生成 | api | 0.3d |
| D'206 | WinForms `LayerAdminForm` に「テーマ編集を WebGIS で開く」ボタン | winforms | 0.1d |

## WD'3 — リアルタイム反映 (SSE + batch UI)

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| D'301 | `EventsEndpoints.cs` 新設: `GET /api/events/layers/{layerId}/stream` (SSE) + Npgsql `LISTEN` connection + replay buffer | api | 0.5d |
| D'302 | `0F02_notify_invalidation.sql`: 既存 7 関数に `pg_notify` 追加 (CREATE OR REPLACE) | db | 0.2d |
| D'303 | `webgis/src/controllers/eventStream.ts`: `EventSource` 購読 + `setBaseLayerSource` 再呼び出し + reconnect handling | webgis | 0.3d |
| D'304 | `webgis/src/api/client.ts` `postFeatureBatch` + WinForms `AttributeEditorControl` の複数選択 batch モード | webgis+winforms | 0.3d |
| D'305 | WinForms `LayerEventListener.cs` 新規 (DI 注入、SSE 受領でステータス表示) | winforms | 0.2d |

## WD'4 — テスト + Docs

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| D'401 | `api.tests`: `FeatureBatchTests`, `EventsStreamTests`, `AttributeStatsTests`, `LayersEndpointsStyleVersionTests` | tests | 0.3d |
| D'402 | `webgis vitest`: `styleEditor.spec.ts`, `colorRamp.spec.ts`, `eventStream.spec.ts` | tests | 0.2d |
| D'403 | `windos-app.tests`: batch 編集モード + `LayerEventListenerTests` | tests | 0.2d |
| D'404 | `docs/PHASE_D_PRIME_INDEX.md` 完了状態 + `rendering.md` 章追加 + `api-events.md` 新設 | docs | 0.2d |
| D'405 | README 更新 + `orchestration_state.md` メモリ更新 | docs | 0.1d |

## 起票時のテンプレート

```markdown
## 課題
(Plan の §X.1 をコピー)

## 採用方針
(Plan の §X.2 採用案をコピー)

## 影響範囲
(Plan の §X.3 をコピー)

## 受入条件
- [ ] (Wave Plan の検証項目)
- [ ] テストが green (`-c Release`)
- [ ] PR 本文に変更点要約

## 関連
- 親 Wave: WD'N (#N)
- Design: docs/XXX.md
```

## マイルストーン

`Phase D': テーマ編集 UI + 即時反映 + 一括編集`

## 並列実行の指針

- 各 Wave 内: 同 Wave 内の独立 Issue は同 PR にまとめる (PR レビュー単位を Wave に揃える)
- Wave 間: 前 Wave マージ後に次 Wave 着手 (依存あり)
- Phase D' 並走 候補:
  - Phase E'/C'/H5 とは並走しない (1 サイクル完了後に次)
  - ただし Plan/Design ドキュメント執筆は前倒し可
