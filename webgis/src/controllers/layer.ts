import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import type { MapContext } from '../map/mapInit';
import { fetchLayers, getCurrentAccessToken, getLayerExtent } from '../api/client';

// D301 (WD3): VectorLayer 経路を廃止し TileLayer(XYZ) で /tiles/{layerId}/{theme}/{z}/{x}/{y}.png を参照
// tileLoadFunction で Authorization: Bearer {jwt} を付与する。jwt は api/client.ts setAccessToken で更新される。
//
// F401 (Phase F WF4): 複数 layer 同時表示の addLayer / removeLayer / setLayerVisible 経路を追加。
// 旧 setBaseLayerSource は @deprecated だが、後方互換 (DOM `<select id="layer-select">` 直接利用) のため残置。

/** 認証付き XYZ source を組み立てる共通 helper。 */
function buildXyzSource(url: string): XYZ {
  return new XYZ({
    url,
    tileLoadFunction: (tile, src) => {
      const image = (tile as unknown as { getImage(): HTMLImageElement }).getImage();
      const token = getCurrentAccessToken();
      if (!token) {
        image.src = src;
        return;
      }
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
}

/** F401: tile URL を組み立てる (theme/asOf/styleVersion を URL に伝搬)。 */
function buildTileUrl(layerId: number, theme: string, asOf: string | null, styleVersion: number | null): string {
  const params = new URLSearchParams();
  if (styleVersion !== null) params.set('sv', String(styleVersion));
  if (asOf) params.set('asOf', asOf);
  const qs = params.toString();
  return `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png${qs ? '?' + qs : ''}`;
}

/**
 * F401 (Phase F WF4): layer を追加 (既に存在する場合は theme/asOf 変更時のみ source 差替)。
 *
 * 戻り値: 追加/更新された TileLayer。
 */
export function addLayer(
  ctx: MapContext,
  layerId: number,
  theme: string = 'default',
  asOf: string | null = null,
  styleVersion: number | null = null
): TileLayer<XYZ> {
  const url = buildTileUrl(layerId, theme, asOf, styleVersion);
  const existing = ctx.layerStack.get(layerId);
  if (existing) {
    // 既存 layer の source 差替 (theme/asOf/sv 変更)
    existing.setSource(buildXyzSource(url));
    existing.setVisible(true);
    ctx.themeByLayer.set(layerId, theme);
    ctx.styleVersionByLayer.set(layerId, styleVersion);
    return existing;
  }
  const tileLayer = new TileLayer<XYZ>({
    source: buildXyzSource(url),
    preload: 2
  });
  // F401: selectionLayer より下、OSM より上に差し込む
  //   ol/Map.getLayers() は順序付き Collection、index 0=OSM。
  //   selectionLayer はスタック最上位を維持したいので、selectionLayer の直前に insert する。
  const allLayers = ctx.map.getLayers();
  const layersArr = allLayers.getArray();
  const selectionIdx = layersArr.indexOf(ctx.selectionLayer);
  if (selectionIdx >= 0) {
    allLayers.insertAt(selectionIdx, tileLayer);
  } else {
    allLayers.push(tileLayer);
  }
  ctx.layerStack.set(layerId, tileLayer);
  ctx.themeByLayer.set(layerId, theme);
  ctx.styleVersionByLayer.set(layerId, styleVersion);
  // 直近に追加された layer を currentLayerId とする (theme_change / asOf 切替の対象)
  ctx.currentLayerId = layerId;
  ctx.currentTheme = theme;
  return tileLayer;
}

/**
 * F401: layer を完全に削除 (map から外して GC 可能に)。
 */
export function removeLayer(ctx: MapContext, layerId: number): void {
  const tileLayer = ctx.layerStack.get(layerId);
  if (!tileLayer) return;
  ctx.map.removeLayer(tileLayer);
  ctx.layerStack.delete(layerId);
  ctx.themeByLayer.delete(layerId);
  ctx.styleVersionByLayer.delete(layerId);
  if (ctx.currentLayerId === layerId) {
    // 残っている layer の最後尾を currentLayerId に
    const remaining = Array.from(ctx.layerStack.keys());
    ctx.currentLayerId = remaining.length > 0 ? remaining[remaining.length - 1] : null;
  }
}

/**
 * F401: layer の表示/非表示 (削除は伴わない、tile cache を残す)。
 */
export function setLayerVisible(ctx: MapContext, layerId: number, visible: boolean): void {
  const tileLayer = ctx.layerStack.get(layerId);
  if (!tileLayer) return;
  tileLayer.setVisible(visible);
}

/** F401: 現在表示中 (Map に登録 + visible=true) の layer_id 一覧を返す。 */
export function getVisibleLayerIds(ctx: MapContext): number[] {
  const result: number[] = [];
  for (const [id, tl] of ctx.layerStack) {
    if (tl.getVisible()) result.push(id);
  }
  return result;
}

/**
 * F405 (Phase F WF4): @deprecated 旧 setBaseLayerSource は wireLayerSelect 経路の互換のため残置。
 * 新規コードは addLayer/removeLayer を使うこと。
 */
export function setBaseLayerSource(
  ctx: MapContext,
  layerId: number,
  theme: string,
  asOf: string | null = null,
  styleVersion: number | null = null
): void {
  // E401 (WE4): URL に ?asOf= を付与
  // D'201 (WD'2): URL に ?sv={styleVersion} を付与 (SLD cache busting、sld-cache-busting.md 参照)
  // 両方付く場合は ?sv=N&asOf=YYYY-MM-DD の形。OL は URL 全体をキャッシュキーに含めるので
  // どちらかが変わると新タイル取得。
  // F405 (deprecated): 本関数は旧 1-layer モードの遺物。新規コードは addLayer/removeLayer を使うこと。
  const url = buildTileUrl(layerId, theme, asOf, styleVersion);
  ctx.baseLayer.setSource(buildXyzSource(url));
  ctx.currentLayerId = layerId;
  ctx.currentTheme = theme;
  ctx.currentAsOf = asOf;
  ctx.currentStyleVersion = styleVersion;
}

// D303 (WD3): theme_change envelope を受領した時に呼ぶ
export function changeTheme(ctx: MapContext, theme: string): void {
  if (ctx.currentLayerId === null) return;
  setBaseLayerSource(ctx, ctx.currentLayerId, theme, ctx.currentAsOf, ctx.currentStyleVersion);
}

// E401 (WE4): asOf_change envelope (Host から日付ピッカー値変更通知) を受領した時
// F401 (Phase F WF4): 全 layerStack の layer について asOf を再設定する
//   (asOf は地図全体で 1 つの時間軸、layer 単位の状態ではない)
export async function changeAsOf(ctx: MapContext, asOf: string | null): Promise<void> {
  ctx.currentAsOf = asOf;
  if (ctx.layerStack.size === 0) return;
  // styleVersion は asOf 時点で active だったものに切り替わるため再取得
  let layers: Array<{ layerId: number; styleVersion: number | null }> = [];
  try {
    const fetched = await fetchLayers(asOf ?? undefined);
    layers = fetched.map(l => ({ layerId: l.layerId, styleVersion: l.styleVersion ?? null }));
  } catch (e) {
    console.warn('[layer] fetchLayers for asOf failed', e);
  }
  for (const layerId of Array.from(ctx.layerStack.keys())) {
    const theme = ctx.themeByLayer.get(layerId) ?? 'default';
    const sv = layers.find(l => l.layerId === layerId)?.styleVersion ?? null;
    // addLayer は既存層に対し source 差替モードで動く
    addLayer(ctx, layerId, theme, asOf, sv);
  }
}

// loadFeatures は名前を残しつつ意味を「layer/theme 差替 + extent fit」に拡張
// E401 (WE4): asOf 引数追加
// D'201 (WD'2): styleVersion を /api/layers から取得して URL に伝搬
// F405 (Phase F WF4): 内部実装を addLayer 経路に切替。
//   - 旧経路: setBaseLayerSource (baseLayer 1 枚に source 差替) — wireLayerSelect で互換維持
//   - 新経路: addLayer (layerStack に積む) — main.ts の layer_visibility_change で使用
// 単数 select (`<select id="layer-select">`) からの遷移では、既存 layer を全て removeLayer
// してから addLayer で 1 枚にする (旧 1-layer モードの UI 期待値を維持)。
export async function loadFeatures(ctx: MapContext, layerId: number, theme: string = 'default', asOf: string | null = null): Promise<void> {
  // styleVersion を取得 (キャッシュ済の場合はそれを使う、無ければ /api/layers で取得)
  let sv: number | null = null;
  try {
    const layers = await fetchLayers(asOf ?? undefined);
    sv = layers.find(l => l.layerId === layerId)?.styleVersion ?? null;
  } catch (e) {
    console.warn('[layer] fetchLayers for styleVersion failed', e);
  }
  ctx.currentAsOf = asOf;
  // 旧 1-layer モード互換: 既存 layer を一旦全削除して新規 1 枚に
  for (const existingId of Array.from(ctx.layerStack.keys())) {
    if (existingId !== layerId) removeLayer(ctx, existingId);
  }
  addLayer(ctx, layerId, theme, asOf, sv);
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
