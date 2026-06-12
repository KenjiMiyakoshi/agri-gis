import { describe, it, expect, beforeEach, vi } from 'vitest';
import TileLayer from 'ol/layer/Tile';
import View from 'ol/View';
import type XYZ from 'ol/source/XYZ';
import type { MapContext } from '../../map/mapInit';
import { addLayer, removeLayer, setLayerVisible, getVisibleLayerIds } from '../layer';

// F401 (Phase F WF4): layerStack 操作の単体テスト。
// 実 ol.Map は document に依存するため、addLayer/removeLayer/setLayerVisible が
// 触る map API (getLayers / removeLayer) のみを満たす最小スタブで代替する。
// この方式により jsdom 依存を避けつつ layerStack 管理ロジックを検証できる。

beforeEach(() => {
  globalThis.fetch = vi.fn(() => Promise.resolve(new Response('', { status: 200 }))) as never;
});

// ol/Map.getLayers() が返す Collection の最小スタブ
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
  const view = new View({ center: [0, 0], zoom: 0 });
  return {
    map,
    view,
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

describe('layer.ts addLayer/removeLayer/setLayerVisible (F401)', () => {
  it('addLayer registers the layer in layerStack and inserts before selectionLayer', () => {
    const ctx = makeMapContext();
    const initialCount = ctx.map.getLayers().getLength();
    expect(ctx.layerStack.size).toBe(0);

    addLayer(ctx, 1, 'default', null, 5);

    expect(ctx.layerStack.has(1)).toBe(true);
    expect(ctx.layerStack.size).toBe(1);
    expect(ctx.map.getLayers().getLength()).toBe(initialCount + 1);
    expect(ctx.themeByLayer.get(1)).toBe('default');
    expect(ctx.styleVersionByLayer.get(1)).toBe(5);
    expect(ctx.currentLayerId).toBe(1);
    // selectionLayer は常にスタック最上位を維持
    const arr = ctx.map.getLayers().getArray();
    expect(arr[arr.length - 1]).toBe(ctx.selectionLayer);
  });

  it('addLayer called twice with same id updates source (no duplicate layer)', () => {
    const ctx = makeMapContext();
    const tl1 = addLayer(ctx, 1, 'default', null, 1);
    const lengthAfterFirst = ctx.map.getLayers().getLength();
    const tl2 = addLayer(ctx, 1, 'byOwner', null, 2);
    expect(ctx.map.getLayers().getLength()).toBe(lengthAfterFirst);
    expect(tl1).toBe(tl2);  // 同じ TileLayer インスタンス
    expect(ctx.themeByLayer.get(1)).toBe('byOwner');
    expect(ctx.styleVersionByLayer.get(1)).toBe(2);
  });

  it('removeLayer drops the layer from map and stack', () => {
    const ctx = makeMapContext();
    addLayer(ctx, 1);
    addLayer(ctx, 2);
    const beforeRemove = ctx.map.getLayers().getLength();

    removeLayer(ctx, 1);

    expect(ctx.layerStack.has(1)).toBe(false);
    expect(ctx.layerStack.has(2)).toBe(true);
    expect(ctx.map.getLayers().getLength()).toBe(beforeRemove - 1);
    expect(ctx.themeByLayer.has(1)).toBe(false);
    // 残った layer 2 が新しい currentLayerId
    expect(ctx.currentLayerId).toBe(2);
  });

  it('removeLayer of last layer resets currentLayerId to null', () => {
    const ctx = makeMapContext();
    addLayer(ctx, 1);
    removeLayer(ctx, 1);
    expect(ctx.currentLayerId).toBeNull();
  });

  it('setLayerVisible toggles TileLayer.getVisible without removing', () => {
    const ctx = makeMapContext();
    const tl = addLayer(ctx, 1);
    expect(tl.getVisible()).toBe(true);

    setLayerVisible(ctx, 1, false);
    expect(tl.getVisible()).toBe(false);
    expect(ctx.layerStack.has(1)).toBe(true); // 削除はされない

    setLayerVisible(ctx, 1, true);
    expect(tl.getVisible()).toBe(true);
  });

  it('getVisibleLayerIds returns only visible (visible=true) layers', () => {
    const ctx = makeMapContext();
    addLayer(ctx, 1);
    addLayer(ctx, 2);
    addLayer(ctx, 3);
    setLayerVisible(ctx, 2, false);

    const visible = getVisibleLayerIds(ctx);
    expect(visible).toContain(1);
    expect(visible).not.toContain(2);
    expect(visible).toContain(3);
  });

  it('setLayerVisible on unknown layerId is no-op', () => {
    const ctx = makeMapContext();
    expect(() => setLayerVisible(ctx, 999, true)).not.toThrow();
  });

  it('removeLayer on unknown layerId is no-op', () => {
    const ctx = makeMapContext();
    const before = ctx.map.getLayers().getLength();
    removeLayer(ctx, 999);
    expect(ctx.map.getLayers().getLength()).toBe(before);
  });
});
