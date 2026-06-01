import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { featureStyle } from './styles';

export function createVectorLayer(): {
  vectorSource: VectorSource;
  vectorLayer: VectorLayer<VectorSource>;
} {
  const vectorSource = new VectorSource();
  const vectorLayer = new VectorLayer({
    source: vectorSource,
    style: featureStyle
  });
  return { vectorSource, vectorLayer };
}
