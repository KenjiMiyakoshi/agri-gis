import { describe, it, expect } from 'vitest';
import type {
  Envelope,
  FeatureClickedPayload,
  LayerSelectPayload
} from '../messages';

describe('Envelope JSON round-trip', () => {
  it('serializes and parses a feature_clicked envelope', () => {
    const original: Envelope<FeatureClickedPayload> = {
      type: 'feature_clicked',
      payload: { entityId: 'abc', layerId: 1, featureId: 99 },
      requestId: 'rid-1'
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<FeatureClickedPayload>;
    expect(back.type).toBe('feature_clicked');
    expect(back.payload.entityId).toBe('abc');
    expect(back.payload.layerId).toBe(1);
    expect(back.payload.featureId).toBe(99);
    expect(back.requestId).toBe('rid-1');
  });

  it('survives omitted requestId', () => {
    const original: Envelope<LayerSelectPayload> = {
      type: 'layer_select',
      payload: { layerId: 2 }
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<LayerSelectPayload>;
    expect(back.type).toBe('layer_select');
    expect(back.payload.layerId).toBe(2);
    expect(back.requestId).toBeUndefined();
  });

  it('serializes optional fields (asOf) when present', () => {
    const original: Envelope<LayerSelectPayload> = {
      type: 'layer_select',
      payload: { layerId: 1, asOf: '2026-05-29' }
    };
    const wire = JSON.stringify(original);
    expect(wire).toContain('"asOf":"2026-05-29"');
    const back = JSON.parse(wire) as Envelope<LayerSelectPayload>;
    expect(back.payload.asOf).toBe('2026-05-29');
  });
});
