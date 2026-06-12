# Phase F' Wave Plan

```
WF'0 (Plan + Design, 0.5d)
   │
   ▼
WF'1 (API SSE multiplex + permission event, 1.0d)
   │       ┌─────────────────┐
   ▼       ▼                 ▼
WF'2 (WebGIS 単一 EventSource, 1.0d) ⫽ WF'3 (WinForms z-order, 1.5d) ⫽ WF'4 (broker publish フック, 0.5d)
   │
   ▼
WF'5 (E2E + Docs + Complete, 0.5d)
```

合計 5.0d、クリティカルパス WF'0 → WF'1 → WF'2 → WF'5 = 3.0d。

## WF'0 — Plan + Design (Gate)

ブランチ: `feature/phase-f-prime-wf0-plan-design`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'100** | `PHASE_F_PRIME_INDEX.md` + `PHASE_F_PRIME_{PLAN,WAVE_PLAN}.md` + Design 2 本 (`sse-multiplex.md` / `tile-invalidation-on-perm.md`) + ノート (`is-shared-semantics.md`) | 0.5d |

## WF'1 — API: SSE multiplex + permission_invalidate event

ブランチ: `feature/phase-f-prime-wf1-api-sse-multiplex`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'101** | `EventsEndpoints` に `GET /api/events/stream-all?layerIds=1,2,3` 新設 (既存 per-layer endpoint は 200 + `Sunset` ヘッダ) | 0.3d |
| **F'102** | `ILayerInvalidationBroker` に `Subscribe(IReadOnlyList<int> layerIds)` 追加。`PostgresLayerInvalidationBroker` で multi-layer subscription を Channel で fan-in | 0.4d |
| **F'103** | `LayerInvalidationEvent.reason` に `'permission'` 追加 + DTO 拡張 (`affectedOrgId?` フィールド) | 0.2d |
| **F'104** | `Broker.PublishPermissionInvalidate(int orgId, IReadOnlyList<int> layerIds)` メソッド追加 | 0.1d |

検証: `dotnet test api.tests` 全 pass + 新規 `StreamAllEndpointTests` (2) / `PermissionInvalidateBrokerTests` (2)

## WF'2 — WebGIS: 単一 EventSource

ブランチ: `feature/phase-f-prime-wf2-webgis-single-eventsource`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'201** | `eventStream.ts` を全面書き換え。`Map<number, EventSource>` → 単一 `EventSource`、`subscribeLayers(ctx, layerIds[])` で再購読 | 0.4d |
| **F'202** | `permission_invalidate` event handler: `fetchLayers` 再取得 → 非許可 layer を `removeLayer` + 残り layer を `addLayer` 経由で source 再生成 | 0.4d |
| **F'203** | `main.ts` で `layer_visibility_change` 受領時に `subscribeLayers` を呼んで購読 layer 集合を更新 | 0.2d |

検証: `npm test -- --run` 全 pass + 新規 `eventStreamMulti.test.ts` (4 件、permission_invalidate + 購読 layer 集合更新)

## WF'3 — WinForms: z-order ドラッグ + 永続化 (WF'1 と並列)

ブランチ: `feature/phase-f-prime-wf3-winforms-zorder`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'301** | DB migration `0F04_user_preference.sql`: `user_preference(user_id UUID, key TEXT, value JSONB, updated_at TIMESTAMPTZ, PRIMARY KEY (user_id, key))` | 0.2d |
| **F'302** | API: `GET/PUT /api/user/preferences/{key}` 新設 (admin/general/guest 全 role 自己リソース) | 0.3d |
| **F'303** | `MainFormController.VisibleLayerIds: HashSet<int>` → `OrderedLayerIds: List<int>` に変更 (uniqueness は controller 内 check) | 0.2d |
| **F'304** | `MainForm.layerList` に drag 実装 (`MouseDown/MouseMove/MouseUp` + `Items.Move`) + `layer_order_change` envelope 送信 | 0.4d |
| **F'305** | `MainForm.OnLoad` で `IApiClient.GetUserPreferenceAsync("layer_order_v1")` → 初期順序復元、`layerList.ItemCheck` で順序保存 (`PutUserPreferenceAsync`) | 0.3d |
| **F'306** | テスト 6 件: `MainFormControllerOrderTests` (3) + `UserPreferenceEndpointTests` (3) | 0.1d |

WF'1 と並列可。F'301 は SQL 単独、F'302 は API、F'303-F'306 は WinForms で完全独立。

検証: `dotnet test windos-app.tests` 全 pass + 新規 6 件、`dotnet test api.tests` 全 pass + 新規 3 件

## WF'4 — API: 権限変更 → broker publish (WF'1 後並列可)

ブランチ: `feature/phase-f-prime-wf4-broker-hook`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'401** | `AdminOrgLayerPermissionsEndpoints` PUT に `broker.PublishPermissionInvalidate(orgId, layerIds)` 追加 | 0.3d |
| **F'402** | テスト 2 件: `PermissionChangeBroadcastTests` (PUT → broker.Publish 呼ばれた + event 配信される) | 0.2d |

## WF'5 — E2E + Docs + Complete サマリ

ブランチ: `feature/phase-f-prime-wf5-e2e-docs`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F'501** | `docs/manual-verification-phase-f-prime.md` 動作確認シナリオ (権限剥奪 → 即時 invalidate / z-order ドラッグ / SSE connection 確認) | 0.3d |
| **F'502** | `docs/PHASE_F_PRIME_COMPLETE.md` + `orchestration_state.md` メモリ更新 + README 更新 | 0.2d |

## 全 PR

| Wave | ブランチ | base |
|------|---------|------|
| WF'0 | `feature/phase-f-prime-wf0-plan-design` | main |
| WF'1 | `feature/phase-f-prime-wf1-api-sse-multiplex` | main |
| WF'2 | `feature/phase-f-prime-wf2-webgis-single-eventsource` | main |
| WF'3 | `feature/phase-f-prime-wf3-winforms-zorder` | main |
| WF'4 | `feature/phase-f-prime-wf4-broker-hook` | main |
| WF'5 | `feature/phase-f-prime-wf5-e2e-docs` | main |

すべて `base=main` (`stacked_pr_pitfall` 参照)。マージ順 WF'0 → WF'1 → (WF'2 / WF'3 / WF'4) → WF'5 推奨。

## リスク

| # | リスク | 緩和 |
|---|------|------|
| R1 | SSE 旧 endpoint 互換性 | deprecated 残置 + `Sunset` ヘッダ |
| R2 | WinForms drag UX バグ | MVP は drop indicator なし、F'' 候補 |
| R3 | user_preference の競合 | 自己リソースで衝突なし、楽観 OK |
| R4 | broker メモリ膨張 | 100 user 想定で問題なし、本番 Redis 化は Phase H 候補 |
| R5 | permission_invalidate での全 layer 再生成ちらつき | OL TileLayer fade で UX 許容 |
