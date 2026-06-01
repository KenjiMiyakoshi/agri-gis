import type { FeatureLike } from 'ol/Feature';
import { Fill, Stroke, Style, Circle as CircleStyle } from 'ol/style';

const pointStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: '#ef4444' }),
    stroke: new Stroke({ color: '#ffffff', width: 1.5 })
  })
});

const polygonStyle = new Style({
  fill: new Fill({ color: 'rgba(56, 189, 248, 0.25)' }),
  stroke: new Stroke({ color: '#0ea5e9', width: 2 })
});

export function featureStyle(feature: FeatureLike): Style {
  const type = feature.getGeometry()?.getType();
  if (type === 'Point' || type === 'MultiPoint') {
    return pointStyle;
  }
  return polygonStyle;
}
