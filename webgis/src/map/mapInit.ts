import OlMap from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import OSM from 'ol/source/OSM';

// D301 (WD3): VectorLayer 削除、TileLayer (base + selection overlay) の 2 layer 構成。
// F401 (Phase F WF4): baseLayer 1 枚から複数 TileLayer のスタックに拡張。
//   layerStack に layer_id → TileLayer をマップして保持。視認順は map.getLayers() の
//   配列順 (後追加が上)。WinForms の CheckedListBox 順序がそのまま反映される。
//
//   旧 baseLayer フィールドは互換のため残置 (setBaseLayerSource 経路を deprecated として保持)。
//
//   currentLayerId は「最後にクリック/選択された layer」のヒント。F405 で意味が弱まる。
export interface MapContext {
  map: OlMap;
  view: View;
  // F405 (deprecated): 旧 setBaseLayerSource 用 base layer (1 枚)。WinForms 統合後は使われない
  baseLayer: TileLayer<XYZ>;
  selectionLayer: TileLayer<XYZ>;
  // F401: 複数 layer のスタック (layer_id → TileLayer)
  // JS の組み込み Map (top-level OL Map alias と区別するため globalThis.Map にアクセス)
  layerStack: globalThis.Map<number, TileLayer<XYZ>>;
  // F401: layer 単位の theme / asOf / styleVersion (per-layer state)
  themeByLayer: globalThis.Map<number, string>;
  styleVersionByLayer: globalThis.Map<number, number | null>;
  // 現在表示中の layer/theme 状態 (theme_change envelope で差替時に参照)
  currentLayerId: number | null;
  currentTheme: string;
  // E401 (WE4): 現在の asOf (YYYY-MM-DD)。null = 現在 (= valid_to='9999-12-31')
  // F401: asOf は全 layer 共通 (時間軸は地図全体で 1 つ)
  currentAsOf: string | null;
  // D'201 (WD'2): 現在の style_version (旧経路用)。F401 では styleVersionByLayer を使う
  currentStyleVersion: number | null;
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

  const map = new OlMap({
    target: targetId,
    layers: [new TileLayer({ source: new OSM() }), baseLayer, selectionLayer],
    view
  });

  return {
    map,
    view,
    baseLayer,
    selectionLayer,
    layerStack: new Map(),
    themeByLayer: new Map(),
    styleVersionByLayer: new Map(),
    currentLayerId: null,
    currentTheme: DEFAULT_THEME,
    currentAsOf: null,
    currentStyleVersion: null
  };
}
