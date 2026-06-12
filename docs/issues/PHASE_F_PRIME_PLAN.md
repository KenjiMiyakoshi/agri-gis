# Phase F' Plan (polish サイクル)

Phase F (`複数レイヤ同時表示 + 組織×レイヤ権限`) を本番運用前に磨くサイクル。Phase F の `Phase F' 申し送り` 6 件のうち 3 件採用。

## 背景

Phase F 完了直後の動作確認 (admin / salesA / prodA matrix) で機能要件は満たされたが、polish レベルの課題が 6 件残っている (`docs/PHASE_F_COMPLETE.md` §「Phase F' 申し送り」)。本サイクルでは特に **本番運用前の必須穴埋め (tile cache invalidation)** + **多 layer 環境のスケーラビリティ (SSE multiplex)** + **UX 必須要素 (z-order)** を優先する。

## 採用判定

| # | 項目 | 判定 | 理由 |
|---|------|------|------|
| 1 | z-order ドラッグ並べ替え UI | **採用** | 複数 layer 重ね表示の UX 完成度に直結。WinForms CheckedListBox に小変更で実装可 |
| 2 | SSE 単一 connection 統合 | **採用** | #3 と密結合 + Chrome 6 connections/origin 制限。多 layer 時に実用上の制約 |
| 3 | tile cache invalidation on permission change | **採用 (必須)** | セキュリティ穴。権限剥奪後 24h 古い tile が見える可能性。本番運用前に塞ぐ必要 |
| 4 | `is_shared` 細粒度設定 | **Design ノートのみ** | 仕様未確定。Phase G まで送って Plan サイクルで明確化 |
| 5 | バルク権限編集 (多組織×多 layer) | **Phase G 送り** | 既存 1 組織×N で運用可能。matrix UI は別 form 設計が必要で工数大 |
| 6 | WinForms 複数 hit 集約 UI | **Phase G 送り** | RLS で feature 集合の意味論が変わる可能性 (他組織 feature の扱い)。先送り合理的 |

## 設計の柱

### 1. SSE multiplex (項目 2 + 3)

- 新 endpoint: `GET /api/events/stream-all?layerIds=1,2,3`
- 既存 broker (`ILayerInvalidationBroker`) は per-layer subscribe → multi-layer subscribe API 追加
- `LayerInvalidationEvent.reason` に `'permission'` を追加 (feature/style/layer/permission の 4 種)
- WebGIS は単一 EventSource + JSON payload の `layerId` で振り分け
- 旧 `/api/events/layers/{id}/stream` は deprecated 残置 (`Sunset` ヘッダ付与、Phase G で削除)

### 2. Tile invalidation on permission change (項目 3)

- `AdminOrgLayerPermissionsEndpoints` PUT 内で `broker.PublishPermissionInvalidate(orgId, affectedLayerIds[])` を fire
- broker は対象 org の全 active session に対し `permission_invalidate` event を配信
- WebGIS 側は受信時:
  1. `fetchLayers` で現在の許可 layer を再取得
  2. `layerStack` の中で許可されなくなった layer を `removeLayer`
  3. 残った layer は `addLayer` 再呼び出しで tile URL 更新 (sv 増分 or sv 同じでも新 URL で OL が cache flush)

### 3. z-order drag + 永続化 (項目 1)

- WinForms `CheckedListBox` は item ドラッグを標準サポートしない → `MouseDown/Move/Up` で手動実装 + `Items.Move(from, to)`
- `MainFormController.VisibleLayerIds` を `HashSet<int>` から **`List<int>` (順序保持)** に変更 (Set 意味を残すため uniqueness check は controller 内で担保)
- Host → Web envelope: `layer_order_change { layerIds: [1, 5, 2] }` (上位が前面)
- WebGIS 側は `ol/Map.getLayers()` で `layerStack` のレイヤを順序通り並べ替え (`selectionLayer` は常に最上位)
- 永続化: `user_preference(user_id, key, value JSONB)` に `key='layer_order_v1'`、`value=[5, 2, 1]` で保存。MainForm.OnLoad で API 経由取得 → CheckedListBox 初期順序に反映

### 4. `is_shared` 仕様ノート (項目 4)

`docs/is-shared-semantics.md` で現状の `layers.is_shared` 列の意味曖昧さ + Phase G で確定すべき仕様候補を列挙する Design ノートのみ作成。実装は Phase G。

## 工数見積

| Wave | 工数 |
|------|------|
| WF'0 (Plan + Design) | 0.5d |
| WF'1 (API SSE multiplex + permission event) | 1.0d |
| WF'2 (WebGIS 単一 EventSource) | 1.0d |
| WF'3 (WinForms z-order + 永続化) | 1.5d |
| WF'4 (API broker publish フック) | 0.5d |
| WF'5 (E2E + Complete) | 0.5d |
| **合計** | **5.0d** |

クリティカルパス: WF'0 → WF'1 → WF'2 → WF'5 = 3.0d。WF'3 と WF'4 は WF'1 完了後に並列可。

## リスク

| # | リスク | 緩和 |
|---|------|------|
| R1 | SSE 旧 endpoint 互換性 | deprecated 注記 + `Sunset` ヘッダ。Phase G まで残置 |
| R2 | 多 user の SSE 同時接続で broker メモリ膨張 | 既存 broker は in-memory dictionary、ユーザ数 100 程度なら問題なし。本番 1000+ は Phase H で Redis 化検討 |
| R3 | WinForms drag UX バグ (drop indicator 表示等) | MVP は drag without indicator、UX 改善は F'' 候補 |
| R4 | user_preference テーブルの schema migration 失敗 | 新規テーブルで idempotent (CREATE TABLE IF NOT EXISTS)、リスク低 |
| R5 | permission_invalidate で WebGIS 全 layer 再生成 → ちらつき | OL TileLayer.setSource() は段階的 fade で UX OK (D'201 でも同じ) |

## 関連

- `PHASE_F_INDEX.md` / `PHASE_F_COMPLETE.md`
- `docs/issues/PHASE_F_PRIME_WAVE_PLAN.md`
- メモリ: `architecture.md` / `stacked_pr_pitfall.md`
