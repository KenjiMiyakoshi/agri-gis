import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import TileLayer from 'ol/layer/Tile';
import View from 'ol/View';
import type XYZ from 'ol/source/XYZ';
import type { MapContext } from '../../map/mapInit';
import { addLayer, getVisibleLayerIds } from '../layer';
import { subscribeLayers, stopAllEventStreams } from '../eventStream';
import { setAccessToken } from '../../api/client';

// F'201/F'202 (Phase F' WF'2): 単一 EventSource + permission_invalidate handler の検証。
// EventSource は DOM API のため最小モックを用意する。

class FakeLayerCollection {
  arr: unknown[] = [];
  getArray() { return this.arr; }
  getLength() { return this.arr.length; }
  push(l: unknown) { this.arr.push(l); }
  insertAt(i: number, l: unknown) { this.arr.splice(i, 0, l); }
  indexOf(l: unknown) { return this.arr.indexOf(l); }
  remove(l: unknown) {
    const i = this.arr.indexOf(l);
    if (i >= 0) this.arr.splice(i, 1);
  }
}

function makeMapContext(): MapContext {
  const baseLayer = new TileLayer<XYZ>({ source: undefined });
  const selectionLayer = new TileLayer<XYZ>({ source: undefined });
  const layers = new FakeLayerCollection();
  layers.push(baseLayer);
  layers.push(selectionLayer);
  const map = {
    getLayers: () => layers as unknown as ReturnType<MapContext['map']['getLayers']>,
    removeLayer: (l: unknown) => layers.remove(l)
  } as unknown as MapContext['map'];
  return {
    map,
    view: new View({ center: [0, 0], zoom: 0 }),
    baseLayer,
    selectionLayer,
    layerStack: new Map(),
    themeByLayer: new Map(),
    styleVersionByLayer: new Map(),
    currentLayerId: null,
    currentTheme: 'default',
    currentAsOf: null,
    currentStyleVersion: null
  };
}

// 最小 EventSource モック
class FakeEventSource {
  static instances: FakeEventSource[] = [];
  url: string;
  listeners = new Map<string, ((e: MessageEvent) => void)[]>();
  onerror: ((e: Event) => void) | null = null;
  closed = false;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }
  addEventListener(type: string, fn: (e: MessageEvent) => void) {
    const arr = this.listeners.get(type) ?? [];
    arr.push(fn);
    this.listeners.set(type, arr);
  }
  close() { this.closed = true; }

  // テスト用 helper
  fire(type: string, payload: object) {
    const arr = this.listeners.get(type) ?? [];
    const ev = { data: JSON.stringify(payload) } as MessageEvent;
    for (const fn of arr) fn(ev);
  }
}

beforeEach(() => {
  FakeEventSource.instances.length = 0;
  (globalThis as { EventSource?: typeof EventSource }).EventSource =
    FakeEventSource as unknown as typeof EventSource;
  globalThis.fetch = vi.fn(() => Promise.resolve(new Response('', { status: 200 }))) as never;
  setAccessToken('test-token');
});

afterEach(() => {
  stopAllEventStreams();
  setAccessToken('');
});

describe('subscribeLayers (F\'201)', () => {
  it('opens a single EventSource for given layerIds', () => {
    const ctx = makeMapContext();
    subscribeLayers(ctx, [1, 2, 3]);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0].url).toContain('/api/events/stream-all?');
    expect(FakeEventSource.instances[0].url).toContain('layerIds=1%2C2%2C3');
  });

  it('does nothing when layerIds is the same set as currently subscribed', () => {
    const ctx = makeMapContext();
    subscribeLayers(ctx, [1, 2]);
    subscribeLayers(ctx, [2, 1]); // same set, different order
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('closes old EventSource and opens new one when set changes', () => {
    const ctx = makeMapContext();
    subscribeLayers(ctx, [1, 2]);
    subscribeLayers(ctx, [1, 2, 3]);
    expect(FakeEventSource.instances).toHaveLength(2);
    expect(FakeEventSource.instances[0].closed).toBe(true);
  });

  it('closes EventSource when empty layerIds passed', () => {
    const ctx = makeMapContext();
    subscribeLayers(ctx, [1, 2]);
    subscribeLayers(ctx, []);
    expect(FakeEventSource.instances[0].closed).toBe(true);
  });
});

describe('permission_invalidate handler (F\'202)', () => {
  it('removes layers that are no longer allowed', async () => {
    const ctx = makeMapContext();
    addLayer(ctx, 1);
    addLayer(ctx, 2);
    addLayer(ctx, 3);

    // fetchLayers が layer 1 のみ許可を返すモック
    globalThis.fetch = vi.fn((url: string | URL | Request) => {
      const u = url.toString();
      if (u.includes('/api/layers')) {
        return Promise.resolve(new Response(JSON.stringify([
          { layerId: 1, layerName: 'L1', layerType: 'polygon', schemaVersion: 1,
            schema: { fields: [] }, ownerOrgId: null, isShared: false,
            createdAt: '2026-06-12T00:00:00Z', styleVersion: 1, canEdit: false }
        ]), { status: 200, headers: { 'Content-Type': 'application/json' } }));
      }
      return Promise.resolve(new Response('', { status: 200 }));
    }) as never;

    subscribeLayers(ctx, getVisibleLayerIds(ctx));
    const src = FakeEventSource.instances[FakeEventSource.instances.length - 1];

    src.fire('layer_invalidate', {
      layerId: 0, reason: 'permission', affectedOrgId: 5, occurredAt: '2026-06-12T01:00:00Z'
    });

    // handler は async なので waiting
    await new Promise(resolve => setTimeout(resolve, 30));

    expect(ctx.layerStack.has(1)).toBe(true);
    expect(ctx.layerStack.has(2)).toBe(false);
    expect(ctx.layerStack.has(3)).toBe(false);
  });

  it('keeps layers that are still allowed', async () => {
    const ctx = makeMapContext();
    addLayer(ctx, 1);
    addLayer(ctx, 2);

    globalThis.fetch = vi.fn(() => Promise.resolve(new Response(JSON.stringify([
      { layerId: 1, layerName: 'L1', layerType: 'polygon', schemaVersion: 1,
        schema: { fields: [] }, ownerOrgId: null, isShared: false,
        createdAt: '2026-06-12T00:00:00Z', styleVersion: 1, canEdit: true },
      { layerId: 2, layerName: 'L2', layerType: 'polygon', schemaVersion: 1,
        schema: { fields: [] }, ownerOrgId: null, isShared: false,
        createdAt: '2026-06-12T00:00:00Z', styleVersion: 1, canEdit: true }
    ]), { status: 200, headers: { 'Content-Type': 'application/json' } }))) as never;

    subscribeLayers(ctx, getVisibleLayerIds(ctx));
    const src = FakeEventSource.instances[FakeEventSource.instances.length - 1];

    src.fire('layer_invalidate', {
      layerId: 0, reason: 'permission', affectedOrgId: 5, occurredAt: '2026-06-12T01:00:00Z'
    });

    await new Promise(resolve => setTimeout(resolve, 30));

    expect(ctx.layerStack.has(1)).toBe(true);
    expect(ctx.layerStack.has(2)).toBe(true);
  });
});
