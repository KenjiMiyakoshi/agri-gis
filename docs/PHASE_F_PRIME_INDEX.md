# Phase F' Index — Polish (SSE multiplex + tile invalidation + z-order)

agri-gis Phase F' (`複数レイヤ同時表示 + 組織×レイヤ権限管理` の polish サイクル) の高位サマリ。Phase F (WF0-WF5) 完了後の改善 wave。

## スコープ

Phase F の `docs/PHASE_F_COMPLETE.md` §「Phase F' 申し送り」から 3 件採用 + 1 件 Design ノートのみ。

| # | 項目 | 採用判定 |
|---|------|---------|
| 1 | z-order ドラッグ並べ替え UI | ✅ 採用 (+ user_preference 永続化) |
| 2 | SSE 単一 connection 統合 | ✅ 採用 |
| 3 | tile cache invalidation on permission change | ✅ 採用 (セキュリティ穴を塞ぐ、必須) |
| 4 | `is_shared` 細粒度設定 | ❌ Design ノートのみ → Phase G 送り |
| 5 | バルク権限編集 (多組織×多 layer) | ❌ Phase G 送り (1組織×N で運用可) |
| 6 | WinForms 複数 hit 集約 UI | ❌ Phase G 送り (RLS で意味論変化のため) |

## 採用方針

| 観点 | 採用 |
|------|------|
| SSE 統合 endpoint | `/api/events/stream-all?layerIds=1,2,3` 新設、旧 `/api/events/layers/{id}/stream` は deprecated 残置 |
| Tile invalidation 方式 | 権限変更 event を `LayerInvalidationEvent.reason='permission'` で配信。WebGIS は `layerStack` 全 layer + `fetchLayers` 再取得 → 非許可 layer は `removeLayer` |
| z-order 保存 | `user_preference(user_id, key, value JSONB)` テーブル新設。`layer_order_v1` キーで `[layerId, ...]` 配列 |
| z-order envelope | `layer_order_change` (Host → Web) で WinForms 操作を WebGIS に伝達 |
| 旧 endpoint 廃止時期 | Phase G で物理削除 (F' は deprecated 注記 + Sunset ヘッダ) |

詳細は `docs/issues/PHASE_F_PRIME_PLAN.md`。

## Wave 構成

| Wave | テーマ | 工数 |
|------|--------|------|
| **WF'0** | Plan + Design 2 本 + `is-shared-semantics.md` ノート | 0.5d |
| **WF'1** | API: `/api/events/stream-all` + `permission_invalidate` event + broker multi-layer 配信 + 旧 endpoint deprecated 注記 | 1.0d |
| **WF'2** | WebGIS: `eventStream.ts` 単一 EventSource 化 + `permission_invalidate` 受信時の全 layer 再生成 | 1.0d |
| **WF'3** | WinForms: CheckedListBox ドラッグ並べ替え + `user_preference` DB 保存 + `layer_order_change` envelope | 1.5d |
| **WF'4** | API: `AdminOrgLayerPermissionsEndpoints` PUT に `broker.PublishPermissionInvalidate` フック | 0.5d |
| **WF'5** | E2E + Complete サマリ + メモリ更新 | 0.5d |
| | **合計** | **5.0d** |

クリティカルパス: WF'0 → WF'1 → WF'2 → WF'5 = 3.0d (WF'3 / WF'4 は WF'1 後並列可)

## 受け入れ条件

1. ✅ `/api/events/stream-all?layerIds=1,2,3` で複数 layer の event を 1 connection で配信
2. ✅ admin が `salesA` の権限を剥奪 → SSE 経由で `salesA` の WebGIS が即時 tile 再生成 (古い tile が消える)
3. ✅ WinForms で layer を CheckedListBox 内ドラッグで並べ替え → WebGIS 側で z-order が反映 (上位がより前面に)
4. ✅ WinForms 再起動後も z-order が `user_preference` から復元
5. ✅ 旧 `/api/events/layers/{id}/stream` は 200 + `Sunset` ヘッダで応答 (deprecated)
6. ✅ api.tests 全 green + 新規 8 件
7. ✅ webgis vitest 全 green + 新規 4 件
8. ✅ windos-app.tests 全 green + 新規 6 件
9. ✅ `orchestration_state.md` メモリ更新

## Phase G 申し送り

- feature-level RLS (異組織 feature の地理重なり対応)
- バルク権限編集 (多組織×多 layer)
- WinForms クリック時の複数 hit 集約 UI
- マルチテナント完全分離 (DB スキーマ分離 / テナント毎の DB)
- 共有レイヤ (`is_shared`) の細粒度権限 (組織グループ単位)
- 旧 `/api/events/layers/{id}/stream` 物理削除

## 関連ドキュメント

- `PHASE_F_INDEX.md` / `PHASE_F_COMPLETE.md`
- `docs/issues/PHASE_F_PRIME_PLAN.md`
- `docs/issues/PHASE_F_PRIME_WAVE_PLAN.md`
- `docs/sse-multiplex.md` (Design)
- `docs/tile-invalidation-on-perm.md` (Design)
- `docs/is-shared-semantics.md` (Design ノート、Phase G 送り)

## 関連メモリ

- `orchestration_state.md` — 進捗
- `architecture.md` — ハイブリッド構成
- `stacked_pr_pitfall.md` — base=main 固定
