import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import OSM from 'ol/source/OSM';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { fromLonLat } from 'ol/proj';
import { createVectorLayer } from './vectorLayer';

export interface MapContext {
  map: Map;
  view: View;
  vectorSource: VectorSource;
  vectorLayer: VectorLayer<VectorSource>;
}

const DEFAULT_CENTER_LONLAT: [number, number] = [143.205, 42.9115];
const DEFAULT_ZOOM = 15;

export function createMap(targetId: string): MapContext {
  const { vectorSource, vectorLayer } = createVectorLayer();

  const view = new View({
    center: fromLonLat(DEFAULT_CENTER_LONLAT),
    zoom: DEFAULT_ZOOM,
    rotation: 0
  });

  const map = new Map({
    target: targetId,
    layers: [new TileLayer({ source: new OSM() }), vectorLayer],
    view
  });

  return { map, view, vectorSource, vectorLayer };
}
