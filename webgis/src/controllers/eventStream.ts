// D'303 (WD'3): SSE (Server-Sent Events) クライアント
// /api/events/layers/{layerId}/stream を購読し、invalidation 通知で TileLayer を再生成。
// 認証は ?access_token= 経由 (EventSource は Authorization ヘッダ送れない)。
//
// F404 (Phase F WF4): 複数 layer 同時購読対応。layerId → EventSource の Map で管理。
//   - startEventStream(ctx, layerId): 該当 layer の購読開始 (既に購読中なら no-op)
//   - stopEventStreamFor(layerId): 該当 layer の購読のみ停止
//   - stopAllEventStreams(): 全 layer の購読停止
// 申し送り (F'): 単一 EventSource で /api/events?layerIds=1,2,3 のような multiplex に統合

import type { MapContext } from '../map/mapInit';
import { addLayer } from './layer';
import { getCurrentAccessToken, fetchLayers } from '../api/client';

export interface LayerInvalidationEvent {
  layerId: number;
  reason: 'feature' | 'style' | 'layer';
  action?: 'insert' | 'update' | 'delete';
  styleVersion?: number;
  occurredAt: string;
}

// F404: 全 layer 共通の debounce timer。短時間連打 (同 layer での連続更新) を 500ms にまとめる。
// 値は layerId → timer handle。
const debounceTimers = new Map<number, number>();
// F404: layerId → EventSource。複数 layer を同時購読。
const sources = new Map<number, EventSource>();

export function startEventStream(ctx: MapContext, layerId: number): void {
  if (sources.has(layerId)) return;  // 既に購読中
  const token = getCurrentAccessToken();
  if (!token) {
    console.warn('[sse] no token, skip subscribe', layerId);
    return;
  }
  const url = `/api/events/layers/${layerId}/stream?access_token=${encodeURIComponent(token)}`;
  const src = new EventSource(url);
  src.addEventListener('layer_invalidate', async (e) => {
    let ev: LayerInvalidationEvent;
    try { ev = JSON.parse((e as MessageEvent).data); }
    catch { return; }
    if (ev.layerId !== layerId) return;
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
  });
  src.onerror = (e) => {
    console.warn('[sse] error (auto reconnect)', layerId, e);
  };
  sources.set(layerId, src);
}

export function stopEventStreamFor(layerId: number): void {
  const src = sources.get(layerId);
  if (src) {
    src.close();
    sources.delete(layerId);
  }
  const timer = debounceTimers.get(layerId);
  if (timer !== undefined) {
    window.clearTimeout(timer);
    debounceTimers.delete(layerId);
  }
}

export function stopAllEventStreams(): void {
  for (const layerId of Array.from(sources.keys())) {
    stopEventStreamFor(layerId);
  }
}

// 後方互換 alias (旧 stopEventStream は単数前提だった)
export const stopEventStream = stopAllEventStreams;
