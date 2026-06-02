import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import OSM from 'ol/source/OSM';
import { fromLonLat } from 'ol/proj';

// D301 (WD3): VectorLayer 削除、TileLayer (base + selection overlay) の 2 layer 構成。
// 編集モード時のみ単一 entity を取得する経路は別途検討 (Phase D' 候補)。
export interface MapContext {
  map: Map;
  view: View;
  baseLayer: TileLayer<XYZ>;
  selectionLayer: TileLayer<XYZ>;
  // 現在表示中の layer/theme 状態 (theme_change envelope で差替時に参照)
  currentLayerId: number | null;
  currentTheme: string;
}

const DEFAULT_CENTER_LONLAT: [number, number] = [143.205, 42.9115];
const DEFAULT_ZOOM = 15;
const DEFAULT_THEME = 'default';

export function createMap(targetId: string): MapContext {
  // base TileLayer: source は layer/theme 確定後に setSource。初期状態は空
  const baseLayer = new TileLayer<XYZ>({
    source: undefined,
    preload: 2
  });

  // selection overlay TileLayer: sid 確定後に setSource
  const selectionLayer = new TileLayer<XYZ>({
    source: undefined,
    preload: 0,
    opacity: 0.85
  });

  const view = new View({
    center: fromLonLat(DEFAULT_CENTER_LONLAT),
    zoom: DEFAULT_ZOOM,
    rotation: 0
  });

  const map = new Map({
    target: targetId,
    layers: [new TileLayer({ source: new OSM() }), baseLayer, selectionLayer],
    view
  });

  return {
    map,
    view,
    baseLayer,
    selectionLayer,
    currentLayerId: null,
    currentTheme: DEFAULT_THEME
  };
}
