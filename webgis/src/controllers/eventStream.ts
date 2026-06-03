// D'303 (WD'3): SSE (Server-Sent Events) クライアント
// /api/events/layers/{layerId}/stream を購読し、invalidation 通知で TileLayer を再生成。
// 認証は ?access_token= 経由 (EventSource は Authorization ヘッダ送れない)。

import type { MapContext } from '../map/mapInit';
import { setBaseLayerSource } from './layer';
import { getCurrentAccessToken, fetchLayers } from '../api/client';

export interface LayerInvalidationEvent {
  layerId: number;
  reason: 'feature' | 'style' | 'layer';
  action?: 'insert' | 'update' | 'delete';
  styleVersion?: number;
  occurredAt: string;
}

let currentSource: EventSource | null = null;
let debounceTimer: number | null = null;

export function startEventStream(ctx: MapContext, layerId: number): void {
  stopEventStream();
  const token = getCurrentAccessToken();
  if (!token) {
    console.warn('[sse] no token, skip subscribe');
    return;
  }
  const url = `/api/events/layers/${layerId}/stream?access_token=${encodeURIComponent(token)}`;
  currentSource = new EventSource(url);
  currentSource.addEventListener('layer_invalidate', async (e) => {
    let ev: LayerInvalidationEvent;
    try { ev = JSON.parse((e as MessageEvent).data); }
    catch { return; }
    if (ev.layerId !== ctx.currentLayerId) return;
    // 短間隔連打を debounce 500ms
    if (debounceTimer !== null) window.clearTimeout(debounceTimer);
    debounceTimer = window.setTimeout(async () => {
      // 注: ctx.currentStyleVersion + LayerDto.styleVersion + setBaseLayerSource の sv 引数は
      // Phase D' WD'2 (#224) で導入される。WD'3 単独ではビルド通らないため as any で逃がす。
      const ctxAny = ctx as any;
      let sv: number | null = ev.styleVersion ?? ctxAny.currentStyleVersion ?? null;
      if (ev.reason === 'feature') {
        try {
          const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
          sv = (layers.find(l => l.layerId === layerId) as any)?.styleVersion ?? sv;
        } catch { }
      }
      ctxAny.currentStyleVersion = sv;
      (setBaseLayerSource as any)(ctx, ctx.currentLayerId!, ctx.currentTheme, ctx.currentAsOf, sv);
    }, 500);
  });
  currentSource.onerror = (e) => {
    // EventSource は内蔵自動 reconnect。一時的エラーはログのみ
    console.warn('[sse] error (auto reconnect)', e);
  };
}

export function stopEventStream(): void {
  if (currentSource !== null) {
    currentSource.close();
    currentSource = null;
  }
  if (debounceTimer !== null) {
    window.clearTimeout(debounceTimer);
    debounceTimer = null;
  }
}
