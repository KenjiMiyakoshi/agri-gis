// D'303 (WD'3): SSE (Server-Sent Events) クライアント
// /api/events/stream-all?layerIds=1,2,3 を購読し、invalidation 通知で TileLayer を再生成。
// 認証は ?access_token= 経由 (EventSource は Authorization ヘッダ送れない)。
//
// F'201/F'202 (Phase F' WF'2): per-layer EventSource (Phase F WF4) を単一 EventSource +
// 複数 layer 購読に書き換え。
//   - subscribeLayers(ctx, layerIds[]): 該当 layerIds で 1 EventSource 確立 (既存とは同集合チェック)
//   - stopAllEventStreams(): 完全停止
// 旧 per-layer 経路 (`startEventStream(ctx, layerId)` / `stopEventStreamFor(layerId)`) は
// 後方互換のため alias を残置するが、内部実装は subscribeLayers に統合する。

import type { MapContext } from '../map/mapInit';
import { addLayer, removeLayer } from './layer';
import { getCurrentAccessToken, fetchLayers } from '../api/client';

export interface LayerInvalidationEvent {
  layerId: number;
  reason: 'feature' | 'style' | 'layer' | 'permission';  // F'103: permission 追加
  action?: 'insert' | 'update' | 'delete';
  styleVersion?: number;
  affectedOrgId?: number;  // F'103: permission のみ
  occurredAt: string;
}

// F'201: 全 layer 共通の debounce timer (layerId → handle)。短時間連打を 500ms にまとめる。
const debounceTimers = new Map<number, number>();
// F'201: 単一 EventSource。null = 未購読
let currentSource: EventSource | null = null;
// F'201: 現在購読中の layerIds (集合比較で再購読要否を判定)
let subscribedLayerIds: number[] = [];

function sameSet(a: number[], b: number[]): boolean {
  if (a.length !== b.length) return false;
  const sa = new Set(a);
  for (const x of b) if (!sa.has(x)) return false;
  return true;
}

/**
 * F'201 (Phase F' WF'2): 指定 layerIds の SSE を 1 EventSource で購読開始。
 * 既に同じ集合で購読中なら no-op。集合が変わったら旧 EventSource を閉じて再確立。
 */
export function subscribeLayers(ctx: MapContext, layerIds: number[]): void {
  if (sameSet(subscribedLayerIds, layerIds)) return;
  closeSource();
  if (layerIds.length === 0) {
    subscribedLayerIds = [];
    return;
  }
  const token = getCurrentAccessToken();
  if (!token) {
    console.warn('[sse] no token, skip subscribe', layerIds);
    return;
  }
  const ids = layerIds.join(',');
  const qs = `layerIds=${encodeURIComponent(ids)}&access_token=${encodeURIComponent(token)}`;
  const src = new EventSource(`/api/events/stream-all?${qs}`);
  src.addEventListener('layer_invalidate', (e) => {
    let ev: LayerInvalidationEvent;
    try { ev = JSON.parse((e as MessageEvent).data); }
    catch { return; }
    void handleInvalidate(ctx, ev);
  });
  src.onerror = (e) => {
    console.warn('[sse] error (auto reconnect)', e);
  };
  currentSource = src;
  subscribedLayerIds = [...layerIds];
}

/** F'201: 全 layer の購読を停止。 */
export function stopAllEventStreams(): void {
  closeSource();
  subscribedLayerIds = [];
  for (const h of debounceTimers.values()) window.clearTimeout(h);
  debounceTimers.clear();
}

function closeSource(): void {
  if (currentSource !== null) {
    currentSource.close();
    currentSource = null;
  }
}

/**
 * F'202 (Phase F' WF'2): invalidate 受信時の処理。
 *   - reason='permission': fetchLayers 再取得 → 許可されなくなった layer を removeLayer +
 *     残った layer を addLayer 経路で source 再生成
 *   - reason='feature'|'style'|'layer': 既存挙動 (該当 layer のみ addLayer で source 再生成)
 */
async function handleInvalidate(ctx: MapContext, ev: LayerInvalidationEvent): Promise<void> {
  if (ev.reason === 'permission') {
    await handlePermissionInvalidate(ctx);
    return;
  }
  // 既存挙動 (feature/style/layer)
  const layerId = ev.layerId;
  if (!ctx.layerStack.has(layerId)) return;
  const existing = debounceTimers.get(layerId);
  if (existing !== undefined) window.clearTimeout(existing);
  const handle = window.setTimeout(async () => {
    debounceTimers.delete(layerId);
    let sv: number | null = ev.styleVersion ?? ctx.styleVersionByLayer.get(layerId) ?? null;
    if (ev.reason === 'feature') {
      try {
        const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
        sv = layers.find(l => l.layerId === layerId)?.styleVersion ?? sv;
      } catch { /* ignore */ }
    }
    const theme = ctx.themeByLayer.get(layerId) ?? ctx.currentTheme;
    addLayer(ctx, layerId, theme, ctx.currentAsOf, sv);
  }, 500);
  debounceTimers.set(layerId, handle);
}

/**
 * F'202: permission_invalidate 受信時の処理。
 *   1. fetchLayers で現在許可されている layer 一覧を再取得
 *   2. layerStack の中で許可されなくなった layer を removeLayer
 *   3. 残った layer は addLayer で source 再生成 (tile cache flush)
 *   4. subscribedLayerIds を更新して再購読 (許可剥奪 layer の SSE を切る)
 */
async function handlePermissionInvalidate(ctx: MapContext): Promise<void> {
  let allowedIds: Set<number>;
  let layerStyleByLid: Map<number, number | null>;
  try {
    const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
    allowedIds = new Set(layers.map(l => l.layerId));
    layerStyleByLid = new Map(layers.map(l => [l.layerId, l.styleVersion ?? null]));
  } catch (e) {
    console.warn('[sse] fetchLayers on permission_invalidate failed', e);
    return;
  }
  const currentVisibleIds = Array.from(ctx.layerStack.keys());
  for (const lid of currentVisibleIds) {
    if (!allowedIds.has(lid)) {
      // 許可剥奪された layer
      removeLayer(ctx, lid);
    } else {
      // 残った layer は source 再生成
      const theme = ctx.themeByLayer.get(lid) ?? ctx.currentTheme;
      const sv = layerStyleByLid.get(lid) ?? null;
      addLayer(ctx, lid, theme, ctx.currentAsOf, sv);
    }
  }
  // 購読集合を残った layer のみに更新 (剥奪 layer の SSE を切る)
  const remaining = Array.from(ctx.layerStack.keys());
  if (!sameSet(subscribedLayerIds, remaining)) {
    subscribedLayerIds = [];  // sameSet を false にして再確立を強制
    subscribeLayers(ctx, remaining);
  }
}

// -------- 後方互換 (旧 per-layer API) --------
// F'201: 内部実装は subscribeLayers に統合。layerStack の現在の集合で再購読する。
export function startEventStream(ctx: MapContext, _layerId: number): void {
  const ids = Array.from(ctx.layerStack.keys());
  subscribeLayers(ctx, ids);
}
export function stopEventStreamFor(_layerId: number): void {
  // 旧 API は per-layer 停止だったが、新実装は集合全体で再購読する。
  // 個別 stop は本来 main.ts の removeLayer 経路で完結するので、ここでは noop。
}
export const stopEventStream = stopAllEventStreams;
