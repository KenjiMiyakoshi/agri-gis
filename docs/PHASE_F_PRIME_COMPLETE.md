# Phase F' Complete — Polish (SSE multiplex + tile invalidation + z-order)

Phase F' (`Phase F の polish サイクル`) の完了サマリ。WF'0〜WF'5 全 Wave マージ済。

## 達成範囲

| Wave | PR | テーマ |
|------|----|-------|
| WF'0 | #246 | Plan + Design 2 本 + is-shared ノート |
| WF'1 | #247 | API: SSE multiplex (`/api/events/stream-all`) + permission_invalidate event + 旧 endpoint Sunset |
| WF'2 | #248 | WebGIS: 単一 EventSource + permission_invalidate handler |
| WF'3 | #250 | WinForms: z-order drag + user_preference 永続化 + WebGIS reorderLayers |
| WF'4 | #249 | API: 権限変更 → broker.PublishPermissionInvalidate フック |
| WF'5 | (本 PR) | E2E + Complete サマリ + メモリ更新 |

## 設計の柱

### 1. SSE multiplex (WF'1 + WF'2)

- **新 endpoint** `GET /api/events/stream-all?layerIds=1,2,3`
  - 単一 EventSource で複数 layer の event を配信
  - 各 layer の `can_view` を検査 (1 件でも不可 → 403)
- **旧 endpoint** `/api/events/layers/{id}/stream` は `Sunset: Sun, 31 Dec 2026 23:59:59 GMT` + `Deprecation: true` + `Link: successor-version` (Phase G で物理削除)
- **broker 拡張**: `SubscribeMultiAsync` / `ReplayRecentMulti` / `PublishPermissionInvalidate`
- **LayerInvalidationEvent**: `reason='permission'` + `affectedOrgId` フィールド追加

### 2. Tile cache invalidation on permission change (WF'4 + WF'2)

- 権限変更時 (`PUT /api/admin/organizations/{orgId}/layer-permissions`) に `broker.PublishPermissionInvalidate(orgId, layerIds)` を fire
- WebGIS は `permission_invalidate` event を受信し:
  1. `fetchLayers` 再取得 (現在の許可 layer 集合)
  2. 許可剥奪された layer は `removeLayer` (TileLayer 解放、tile cache flush)
  3. 残った layer は `addLayer` で source 再生成
  4. SSE 購読集合も更新
- **セキュリティ穴埋め**: Phase F では tile cache TTL 24h で剥奪後も古い tile が見えていた

### 3. z-order drag + 永続化 (WF'3)

- **DB**: `user_preference(user_id, key, value JSONB)` テーブル
- **API**: `GET/PUT /api/user/preferences/{key}` (自己リソース、全 role)
- **WinForms**: CheckedListBox の MouseDown/Move/DragOver/DragDrop で drag-and-drop 実装
  - LayerListItem wrapper で LayerDto を持つ
  - drag 完了時に `Controller.ReorderLayers` + bridge `layer_order_change` + 永続化
- **WebGIS**: `reorderLayers(ctx, orderedIds)` で全 TileLayer を解放→ orderedIds 順で `selectionLayer` 直前に再挿入
- **再起動復元**: OnLoad で `LoadLayerOrderAsync` → `ApplyPersistedLayerOrder` で UI 再構築

## 受け入れ条件

| # | 条件 | 検証 |
|---|------|------|
| 1 | `/api/events/stream-all?layerIds=1,2,3` で複数 layer の event を 1 connection で配信 | api.tests `StreamAllEndpointTests` + S1 manual |
| 2 | 権限剥奪 → SSE 経由で WebGIS が即時 tile 再生成 | api.tests `PermissionChangeBroadcastTests` + webgis `eventStreamMulti.test.ts` + S2 manual |
| 3 | CheckedListBox 内ドラッグで z-order が WebGIS に反映 | webgis 全体 + S4 manual |
| 4 | 再起動後も z-order が `user_preference` から復元 | windos-app.tests `MainFormControllerOrderTests` + S4 manual |
| 5 | 旧 `/api/events/layers/{id}/stream` に Sunset ヘッダ | api.tests `StreamAllEndpointTests.OldEndpoint_HasSunsetHeaderInSource` + S6 manual |
| 6 | api.tests 全 green + 新規 14 件 | ✅ 116 件 pass (102 + 14) |
| 7 | webgis vitest 全 green + 新規 6 件 | ✅ 37 件 pass (31 + 6) |
| 8 | windos-app.tests 全 green + 新規 3 件 | ✅ 174 件 pass (171 + 3) |
| 9 | `orchestration_state.md` メモリ更新 | 本 WF'5 で |

すべて達成。

## テスト件数 (合計 23 件 新規)

| プロジェクト | Phase F 完了時 | Phase F' 追加 | 完了時 |
|---|---|---|---|
| api.tests | 102 | +14 | **116 pass** |
| webgis vitest | 31 | +6 | **37 pass** |
| windos-app.tests | 171 | +3 | **174 pass** |
| **計** | 304 | +23 | **327** |

(api.tests 内訳: Events 系 10 + UserPreference 4)

## Phase F'' / G 申し送り

### Phase G (前回引き継ぎを継続)

- **feature-level RLS** (Row Level Security): 異組織 feature の地理重なり対応
- マルチテナント完全分離 (DB スキーマ分離 / テナント毎の DB)
- `is_shared` 細粒度設定 (Design ノートは Phase F' で作成済)
- 共有レイヤの細粒度権限 (組織グループ単位)
- 旧 `/api/events/layers/{id}/stream` 物理削除 (Sunset 期限 2026-12-31)
- バルク権限編集 (多組織×多 layer)
- WinForms クリック時の複数 hit 集約 UI

### Phase F'' (Phase F' で残った polish)

- z-order ドラッグ時の drop indicator (現状は indicator なし)
- SSE Redis 化 (本番 1000+ user 想定、現状は in-memory broker)
- WinForms クリック時の複数 layer hit 集約 UI (RLS と独立に着手可能)
- `is_shared` 仕様確定 (Phase G の Plan サイクル相当)

## 副次成果

### `ApiFactory.extraConfigure` (WF'4 同梱)

個別テストから service 差し替えできるフック追加。本 PR では broker spy のため、今後の SSE / broker 系テストでも再利用可。

### `FakeApiClient` 拡張 (WF'3 同梱)

`Preferences: Dictionary<string, UserPreferenceDto>` で user_preference を in-memory stub。今後の preference 系 ViewModel テストで再利用可。

### `MainFormController.OrderedLayerIds` (WF'3)

H5-101 で抽出した `MainFormController` を更に拡張。順序付き layer 管理 + 永続化経路で multi-layer UI の基盤として完成度向上。

## 関連ドキュメント

- `PHASE_F_PRIME_INDEX.md`
- `docs/issues/PHASE_F_PRIME_PLAN.md` / `PHASE_F_PRIME_WAVE_PLAN.md`
- `docs/sse-multiplex.md` (Design)
- `docs/tile-invalidation-on-perm.md` (Design)
- `docs/is-shared-semantics.md` (Phase G 送り Design ノート)
- `docs/manual-verification-phase-f-prime.md` (F'501 E2E シナリオ)
- `docs/PHASE_F_COMPLETE.md` (前 Phase 完了サマリ)
