import XYZ from 'ol/source/XYZ';
import type { MapContext } from '../map/mapInit';
import { fetchLayers, getCurrentAccessToken, getLayerExtent } from '../api/client';

// D301 (WD3): VectorLayer 経路を廃止し TileLayer(XYZ) で /tiles/{layerId}/{theme}/{z}/{x}/{y}.png を参照
// tileLoadFunction で Authorization: Bearer {jwt} を付与する。jwt は api/client.ts setAccessToken で更新される。

export function setBaseLayerSource(ctx: MapContext, layerId: number, theme: string, asOf: string | null = null): void {
  // E401 (WE4): URL に ?asOf= を付与。OL は ?xxx 部分も内部キャッシュキーに含めるので
  // asOf 切替時に source 自体を作り直すことで cache invalidation。
  const qs = asOf ? `?asOf=${encodeURIComponent(asOf)}` : '';
  const url = `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png${qs}`;
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
  ctx.currentAsOf = asOf;
}

// D303 (WD3): theme_change envelope を受領した時に呼ぶ
export function changeTheme(ctx: MapContext, theme: string): void {
  if (ctx.currentLayerId === null) return;
  setBaseLayerSource(ctx, ctx.currentLayerId, theme, ctx.currentAsOf);
}

// E401 (WE4): asOf_change envelope (Host から日付ピッカー値変更通知) を受領した時
export async function changeAsOf(ctx: MapContext, asOf: string | null): Promise<void> {
  if (ctx.currentLayerId === null) {
    ctx.currentAsOf = asOf;
    return;
  }
  await loadFeatures(ctx, ctx.currentLayerId, ctx.currentTheme, asOf);
}

// loadFeatures は名前を残しつつ意味を「layer/theme 差替 + extent fit」に拡張
// E401 (WE4): asOf 引数追加。layer/theme/asOf 全部含めて差替
export async function loadFeatures(ctx: MapContext, layerId: number, theme: string = 'default', asOf: string | null = null): Promise<void> {
  setBaseLayerSource(ctx, layerId, theme, asOf);
  try {
    const ext = await getLayerExtent(layerId, asOf ?? undefined);
    if (ext.extent3857 && ext.count > 0) {
      ctx.view.fit(ext.extent3857, { padding: [40, 40, 40, 40], maxZoom: 18 });
    }
  } catch (e) {
    console.error('[layer] fit extent failed', e);
  }
}

export async function wireLayerSelect(ctx: MapContext): Promise<void> {
  const select = document.getElementById('layer-select') as HTMLSelectElement | null;
  if (!select) return;

  try {
    const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
    select.innerHTML = '';
    for (const l of layers) {
      const opt = document.createElement('option');
      opt.value = String(l.layerId);
      opt.textContent = `${l.layerId}: ${l.layerName} (${l.layerType})`;
      select.appendChild(opt);
    }
    select.addEventListener('change', () => {
      void loadFeatures(ctx, Number(select.value), ctx.currentTheme, ctx.currentAsOf);
    });
    if (layers.length > 0) {
      select.value = String(layers[0].layerId);
      void loadFeatures(ctx, layers[0].layerId, ctx.currentTheme, ctx.currentAsOf);
    }
  } catch (e) {
    console.error('wireLayerSelect', e);
  }
}
