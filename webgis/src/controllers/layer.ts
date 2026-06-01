import GeoJSON from 'ol/format/GeoJSON';
import type { MapContext } from '../map/mapInit';
import { fetchFeatures, fetchLayers } from '../api/client';

const geoJsonFormat = new GeoJSON({
  dataProjection: 'EPSG:4326',
  featureProjection: 'EPSG:3857'
});

export async function loadFeatures(ctx: MapContext, layerId: number): Promise<void> {
  try {
    const fc = await fetchFeatures(layerId);
    ctx.vectorSource.clear();
    ctx.vectorSource.addFeatures(geoJsonFormat.readFeatures(fc));

    const extent = ctx.vectorSource.getExtent();
    if (extent && extent.every((v) => Number.isFinite(v)) && extent[0] !== extent[2]) {
      ctx.view.fit(extent, { padding: [40, 40, 40, 40], maxZoom: 18 });
    }
  } catch (e) {
    console.error('loadFeatures', e);
  }
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
      void loadFeatures(ctx, Number(select.value));
    });
    if (layers.length > 0) {
      select.value = String(layers[0].layerId);
      await loadFeatures(ctx, layers[0].layerId);
    }
  } catch (e) {
    console.error('wireLayerSelect', e);
  }
}
