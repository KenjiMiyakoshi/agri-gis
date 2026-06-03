import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import OSM from 'ol/source/OSM';

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
  // E401 (WE4): 現在の asOf (YYYY-MM-DD)。null = 現在 (= valid_to='9999-12-31')
  currentAsOf: string | null;
}

// hotfix 2件目 (2026-06-03 朝の動作確認):
// fromLonLat の sphere/ellipsoid Mercator 不一致でデータ位置とずれが出るため、
// 3857 値で直接 center を指定 (帯広駅付近の seed feature が画面内に来る座標)。
// PostGIS の ST_Transform で計算した layer_id=1 中心: (15941563, 5298510)
const DEFAULT_CENTER_3857: [number, number] = [15941563, 5298510];
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
    center: DEFAULT_CENTER_3857,
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
    currentTheme: DEFAULT_THEME,
    currentAsOf: null
  };
}
