import XYZ from 'ol/source/XYZ';
import type { MapContext } from '../map/mapInit';
import { fetchLayers, getCurrentAccessToken } from '../api/client';

// D301 (WD3): VectorLayer 経路を廃止し TileLayer(XYZ) で /tiles/{layerId}/{theme}/{z}/{x}/{y}.png を参照
// tileLoadFunction で Authorization: Bearer {jwt} を付与する。jwt は api/client.ts setAccessToken で更新される。

export function setBaseLayerSource(ctx: MapContext, layerId: number, theme: string): void {
  const url = `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png`;
  const source = new XYZ({
    url,
    tileLoadFunction: (tile, src) => {
      const image = (tile as unknown as { getImage(): HTMLImageElement }).getImage();
      const token = getCurrentAccessToken();
      if (!token) {
        // fallback: src を直接代入 (anonymous)
        image.src = src;
        return;
      }
      // fetch 経由で Authorization ヘッダを付け、blob URL に置換
      fetch(src, { headers: { Authorization: `Bearer ${token}` } })
        .then((r) => {
          if (!r.ok) throw new Error(`tile fetch failed: ${r.status}`);
          return r.blob();
        })
        .then((blob) => {
          const obj = URL.createObjectURL(blob);
          image.src = obj;
          image.onload = () => URL.revokeObjectURL(obj);
        })
        .catch((e) => {
          console.error('[tile]', e);
        });
    },
    crossOrigin: 'anonymous'
  });
  ctx.baseLayer.setSource(source);
  ctx.currentLayerId = layerId;
  ctx.currentTheme = theme;
}

// D303 (WD3): theme_change envelope を受領した時に呼ぶ
export function changeTheme(ctx: MapContext, theme: string): void {
  if (ctx.currentLayerId === null) return;
  setBaseLayerSource(ctx, ctx.currentLayerId, theme);
}

// loadFeatures は名前を残しつつ意味を「layer/theme 差替」に縮退
export function loadFeatures(ctx: MapContext, layerId: number, theme: string = 'default'): void {
  setBaseLayerSource(ctx, layerId, theme);
}

export async function wireLayerSelect(ctx: MapContext): Promise<void> {
  const select = document.getElementById('layer-select') as HTMLSelectElement | null;
  if (!select) return;

  try {
    const layers = await fetchLayers();
    select.innerHTML = '';
    for (const l of layers) {
      const opt = document.createElement('option');
      opt.value = String(l.layerId);
      opt.textContent = `${l.layerId}: ${l.layerName} (${l.layerType})`;
      select.appendChild(opt);
    }
    select.addEventListener('change', () => {
      loadFeatures(ctx, Number(select.value), ctx.currentTheme);
    });
    if (layers.length > 0) {
      select.value = String(layers[0].layerId);
      loadFeatures(ctx, layers[0].layerId, ctx.currentTheme);
    }
  } catch (e) {
    console.error('wireLayerSelect', e);
  }
}
