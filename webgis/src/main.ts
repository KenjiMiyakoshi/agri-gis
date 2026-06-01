import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import OSM from 'ol/source/OSM';
import GeoJSON from 'ol/format/GeoJSON';
import { fromLonLat } from 'ol/proj';
import { Fill, Stroke, Style, Circle as CircleStyle } from 'ol/style';

interface Layer {
  layerId: number;
  layerName: string;
  layerType: string;
}

const API_BASE = '/api';

const vectorSource = new VectorSource();

const vectorLayer = new VectorLayer({
  source: vectorSource,
  style: (feature) => {
    const type = feature.getGeometry()?.getType();
    if (type === 'Point' || type === 'MultiPoint') {
      return new Style({
        image: new CircleStyle({
          radius: 6,
          fill: new Fill({ color: '#ef4444' }),
          stroke: new Stroke({ color: '#ffffff', width: 1.5 })
        })
      });
    }
    return new Style({
      fill: new Fill({ color: 'rgba(56, 189, 248, 0.25)' }),
      stroke: new Stroke({ color: '#0ea5e9', width: 2 })
    });
  }
});

const view = new View({
  center: fromLonLat([143.205, 42.9115]),
  zoom: 15,
  rotation: 0
});

const map = new Map({
  target: 'map',
  layers: [new TileLayer({ source: new OSM() }), vectorLayer],
  view
});

const geoJsonFormat = new GeoJSON({
  dataProjection: 'EPSG:4326',
  featureProjection: 'EPSG:3857'
});

async function loadFeatures(layerId: number): Promise<void> {
  const res = await fetch(`${API_BASE}/features?layerId=${layerId}`);
  if (!res.ok) {
    console.error('features fetch failed', res.status);
    return;
  }
  const fc = await res.json();
  vectorSource.clear();
  vectorSource.addFeatures(geoJsonFormat.readFeatures(fc));

  const extent = vectorSource.getExtent();
  const allFinite = extent.every((v) => Number.isFinite(v));
  if (allFinite && extent[0] !== extent[2]) {
    view.fit(extent, { padding: [40, 40, 40, 40], maxZoom: 18 });
  }
}

async function loadLayers(): Promise<void> {
  const select = document.getElementById('layer-select') as HTMLSelectElement;
  const res = await fetch(`${API_BASE}/layers`);
  if (!res.ok) {
    console.error('layers fetch failed', res.status);
    return;
  }
  const layers = (await res.json()) as Layer[];
  select.innerHTML = '';
  for (const l of layers) {
    const opt = document.createElement('option');
    opt.value = String(l.layerId);
    opt.textContent = `${l.layerId}: ${l.layerName} (${l.layerType})`;
    select.appendChild(opt);
  }
  select.addEventListener('change', () => {
    void loadFeatures(Number(select.value));
  });
  if (layers.length > 0) {
    select.value = String(layers[0].layerId);
    await loadFeatures(layers[0].layerId);
  }
}

function wireRotation(): void {
  const input = document.getElementById('rotation-input') as HTMLInputElement;
  const label = document.getElementById('rotation-value') as HTMLSpanElement;
  input.addEventListener('input', () => {
    const deg = Number(input.value);
    label.textContent = String(deg);
    view.setRotation((deg * Math.PI) / 180);
  });
  view.on('change:rotation', () => {
    const deg = Math.round((view.getRotation() * 180) / Math.PI);
    input.value = String(deg);
    label.textContent = String(deg);
  });
}

wireRotation();
void loadLayers();
