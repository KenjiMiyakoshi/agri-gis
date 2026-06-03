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
      let sv: number | null = ev.styleVersion ?? ctx.currentStyleVersion;
      if (ev.reason === 'feature') {
        // feature 編集の場合は style_version 不変だが、URL 一意性のため再取得
        try {
          const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
          sv = layers.find(l => l.layerId === layerId)?.styleVersion ?? sv;
        } catch { /* ignore */ }
      }
      ctx.currentStyleVersion = sv;
      setBaseLayerSource(ctx, ctx.currentLayerId!, ctx.currentTheme, ctx.currentAsOf, sv);
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
