# Phase D' Wave Plan

## クリティカルパス

```
WD'0 (Design 4 本, 0.5d)
   │
   ▼
WD'1 (DB+API: styleVersion + batch + stats, 1.5d)
   │       ┌──────────────────┐
   ▼       ▼                  ▼
WD'2 (WebGIS admin UI: Monaco + colorRamp, 2.0d)
   │       │
   ▼       ▼
WD'3 (リアルタイム反映: SSE + batch UI, 1.5d)
   │
   ▼
WD'4 (テスト + Docs, 1.0d)
```

合計 6.5d、4 営業日に並列圧縮可能だが Wave 単位の PR レビュー遅延を見込んで 6 営業日 + バッファ。

## WD'0 — Plan + Design (Gate)

ブランチ: `feature/phase-d-prime-wd0-design`

| Issue | 内容 | 工数 |
|-------|------|------|
| **D'100** | Plan 3 本 + Design 4 本作成: `PHASE_D_PRIME_INDEX.md`, `issues/PHASE_D_PRIME_{PLAN,WAVE_PLAN,ISSUES_INDEX}.md`, `sld-cache-busting.md`, `admin-style-editor.md`, `feature-batch-update.md`, `feature-events-sse.md` | 0.5d |

検証:
- 全 7 ドキュメント lint pass (markdown)
- `PHASE_D_PRIME_INDEX.md` と他 Phase Index の構造が揃ってる
- PR 本文に 4 Design 採用案の要点コピー、レビュアー意見を集約

## WD'1 — DB + API 基盤

ブランチ: `feature/phase-d-prime-wd1-api`

| Issue | 内容 | 工数 |
|-------|------|------|
| **D'101** | `GET /api/layers` レスポンスに `styleVersion` 追加 + `AdminLayerDto` も同様 | 0.2d |
| **D'102** | `TilesEndpoints.cs` の `Cache-Control` を `max-age=86400, immutable` (asOf 時 `no-store` 維持) | 0.2d |
| **D'103** | DB migration `0F01_fn_feature_batch_update.sql`: 一括更新関数 (all-or-nothing, 楽観ロック, audit_log) | 0.5d |
| **D'104** | `POST /api/features:batch` 新設 + DTO 3 本 + `FeatureBatch*` 関連クラス | 0.4d |
| **D'105** | `GET /api/admin/layers/{id}/attributes/{field}/stats?bins=N&method=quantile\|equal` 新設、PostgreSQL `ntile()` で実装 | 0.2d |

検証:
- `dotnet test api.tests -c Release` 全 green (新規 `LayersEndpointsStyleVersionTests`, `FeatureBatchTests`, `AttributeStatsTests`)
- 既存 Phase E 関連テスト全 green (退行なし)
- 手動: `GET /api/layers` で `styleVersion` 値、PUT style 後に +1
- 手動: `POST /api/features:batch` で 10 件まとめ更新成功、1 件 mismatch で 409 + rollback

並列度: D'101/D'102/D'105 は完全独立、D'103 → D'104 順次。

## WD'2 — WebGIS 管理 UI

ブランチ: `feature/phase-d-prime-wd2-ui`

| Issue | 内容 | 工数 |
|-------|------|------|
| **D'201** | `setBaseLayerSource` の URL に `?sv=${styleVersion}` 追加、`LayerDto.styleVersion` を `fetchLayers` から伝搬、`loadFeatures` シグネチャ拡張 (`?asOf=` と共存) | 0.2d |
| **D'202** | Admin theme editor `webgis/src/admin/styleEditor.ts` + `admin-style.html` エントリ (Monaco loader CDN) | 0.5d |
| **D'203** | ライブプレビュー: 同画面右ペインに OL map、debounce 500ms で PUT → `style_version+1` → タイル URL 更新 → preview 再描画 | 0.4d |
| **D'204** | カラーランプ UI `webgis/src/admin/colorRamp.ts`: 属性選択 + bins + 色パレット + ヒストグラム svg + `GET /attributes/{field}/stats` 呼び出し | 0.5d |
| **D'205** | `SldXmlBuilder.cs` 拡張: `colorRamp` JSON 受領 → N 段 `<Rule>` 生成 (`<ogc:PropertyIsLessThan>` フィルタ) | 0.3d |
| **D'206** | WinForms `LayerAdminForm` に「テーマ編集を WebGIS で開く」ボタン (`http://localhost:5173/admin-style.html?layerId=N`) | 0.1d |

検証:
- `npm run build` (webgis) 成功、`admin-style.html` バンドルが 1 般 bundle と分離
- 手動: admin ログイン → `/admin-style.html` → SLD 編集 → 保存 → MainForm WebView2 で**手動 reload なしで色更新**
- 手動: カラーランプで `harvest_qty` 5 階級 Viridis → 各階級の色表示

並列度: D'201 を先行、D'202+D'203 セット、D'204+D'205 セット、D'206 最後。

## WD'3 — リアルタイム反映 (SSE + batch UI)

ブランチ: `feature/phase-d-prime-wd3-events`

| Issue | 内容 | 工数 |
|-------|------|------|
| **D'301** | `api/Endpoints/EventsEndpoints.cs` 新設: `GET /api/events/layers/{layerId}/stream` (SSE) + Npgsql `LISTEN agri_gis_layer_invalidate` purpose-built connection + in-memory replay buffer (5s) | 0.5d |
| **D'302** | DB migration `0F02_notify_invalidation.sql`: 既存 7 関数 (`fn_feature_insert/update/delete/fn_layer_style_upsert/fn_layer_update/fn_layer_delete_v2/fn_layer_schema_upsert`) に `pg_notify` 追加 | 0.2d |
| **D'303** | `webgis/src/controllers/eventStream.ts`: `EventSource` 購読 + `setBaseLayerSource` 再呼び出し + reconnect handling | 0.3d |
| **D'304** | `webgis/src/api/client.ts` に `postFeatureBatch` + WinForms `AttributeEditorControl` に 複数選択 batch モード | 0.3d |
| **D'305** | WinForms `LayerEventListener.cs` 新規 (DI 注入、MainForm からは依存受領)。SSE 受領で `属性更新: 3 件 / N 秒前` ステータス表示 | 0.2d |

検証:
- `dotnet test api.tests -c Release` 全 green (新規 `EventsStreamTests`)
- 手動: WinForms 属性編集 → 保存 → 1 秒以内 WebGIS で**自動的に**色更新
- 手動: 10 件まとめ編集 → 1 リクエストで完了 (Network 確認)

並列度: D'301+D'302 セット、D'303 → D'305、D'304 は独立。

## WD'4 — テスト + Docs

ブランチ: `feature/phase-d-prime-wd4-tests-docs`

| Issue | 内容 | 工数 |
|-------|------|------|
| **D'401** | `api.tests` 追加: `FeatureBatchTests` (success/partial-failure rollback/auth), `EventsStreamTests` (LISTEN/NOTIFY を testcontainer で検証), `AttributeStatsTests`, `LayersEndpointsStyleVersionTests` | 0.3d |
| **D'402** | `webgis vitest` 追加: `styleEditor.spec.ts`, `colorRamp.spec.ts`, `eventStream.spec.ts` | 0.2d |
| **D'403** | `windos-app.tests` 追加: batch 編集モード, `LayerEventListenerTests` | 0.2d |
| **D'404** | `docs/PHASE_D_PRIME_INDEX.md` 完了状態に更新 + `docs/rendering.md` に admin style editor 章追加 + `docs/api-events.md` (SSE プロトコル仕様) 新設 | 0.2d |
| **D'405** | README 更新 + `orchestration_state.md` メモリ更新 | 0.1d |

検証:
- 全 3 テストスイート green
- WinForms `-c Release` 起動 → smoke 全項目 OK
- Phase D' 受入条件 12 件全 pass

並列度: D'401-D'403 並列、D'404+D'405 並列。

## 全 PR

| Wave | ブランチ | 想定 PR | base |
|------|---------|---------|------|
| WD'0 | `feature/phase-d-prime-wd0-design` | Phase D' WD'0: Plan + Design 4 本 | main |
| WD'1 | `feature/phase-d-prime-wd1-api` | Phase D' WD'1: DB + API 基盤 | main |
| WD'2 | `feature/phase-d-prime-wd2-ui` | Phase D' WD'2: WebGIS 管理 UI | main |
| WD'3 | `feature/phase-d-prime-wd3-events` | Phase D' WD'3: SSE + batch UI | main |
| WD'4 | `feature/phase-d-prime-wd4-tests-docs` | Phase D' WD'4: Tests + Docs | main |

すべて **base=main** (`stacked_pr_pitfall` メモリ参照)。後続 Wave のブランチを切るタイミングは前 Wave マージ後 (依存あり)。

## リスク

- **R1**: Npgsql の `LISTEN` connection が長時間アイドル断 → keepalive 設定 + auto reconnect。D'301 で扱う
- **R2**: Monaco bundle (1MB+) が一般 WebGIS にバンドル混入 → `admin-style.html` を Vite 別エントリに分離、CDN 経由 dynamic import
- **R3**: SSE が複数 API インスタンスで動かない → 本番運用 (Phase H) で Redis pub-sub に切替、Phase D' 段階では 1 instance 前提
- **R4**: ColorRamp の bin 計算が大データ (1M 行) で遅い → `GET /attributes/{field}/stats` 内で `LIMIT 50000` sampling + `IS NOT NULL` フィルタ
- **R5**: 編集→ `pg_notify` → SSE → タイル再要求が短間隔連打 → WebGIS 側で受領後 500ms debounce
