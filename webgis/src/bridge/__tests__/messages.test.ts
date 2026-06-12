import { describe, it, expect } from 'vitest';
import type {
  Envelope,
  FeaturesSelectedPayload,
  LayerSelectPayload,
  LayerVisibilityChangePayload,
  ThemeChangePayload,
  SelectionOverlayReadyPayload
} from '../messages';

describe('Envelope JSON round-trip', () => {
  it('serializes and parses a features_selected envelope (D303)', () => {
    const original: Envelope<FeaturesSelectedPayload> = {
      type: 'features_selected',
      payload: {
        entityIds: ['abc', 'def'],
        sid: '11111111-2222-3333-4444-555555555555',
        layerId: 1
      },
      requestId: 'rid-1'
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<FeaturesSelectedPayload>;
    expect(back.type).toBe('features_selected');
    expect(back.payload.entityIds).toEqual(['abc', 'def']);
    expect(back.payload.sid).toBe('11111111-2222-3333-4444-555555555555');
    expect(back.payload.layerId).toBe(1);
    expect(back.requestId).toBe('rid-1');
  });

  it('serializes a theme_change envelope (D303)', () => {
    const original: Envelope<ThemeChangePayload> = {
      type: 'theme_change',
      payload: { layerId: 2, theme: 'byOwner' }
    };
    const wire = JSON.stringify(original);
    expect(wire).toContain('"theme":"byOwner"');
    const back = JSON.parse(wire) as Envelope<ThemeChangePayload>;
    expect(back.payload.theme).toBe('byOwner');
  });

  it('serializes a selection_overlay_ready envelope (D303)', () => {
    const original: Envelope<SelectionOverlayReadyPayload> = {
      type: 'selection_overlay_ready',
      payload: { sid: 'sid-uuid', count: 42 }
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<SelectionOverlayReadyPayload>;
    expect(back.payload.count).toBe(42);
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

  it('serializes optional fields (theme, asOf) when present (D303)', () => {
    const original: Envelope<LayerSelectPayload> = {
      type: 'layer_select',
      payload: { layerId: 1, theme: 'default', asOf: '2026-05-29' }
    };
    const wire = JSON.stringify(original);
    expect(wire).toContain('"asOf":"2026-05-29"');
    expect(wire).toContain('"theme":"default"');
    const back = JSON.parse(wire) as Envelope<LayerSelectPayload>;
    expect(back.payload.asOf).toBe('2026-05-29');
    expect(back.payload.theme).toBe('default');
  });

  // F402 (Phase F WF4): layer_visibility_change envelope
  it('serializes layer_visibility_change visible=true with theme (F402)', () => {
    const original: Envelope<LayerVisibilityChangePayload> = {
      type: 'layer_visibility_change',
      payload: { layerId: 5, visible: true, theme: 'default' }
    };
    const wire = JSON.stringify(original);
    expect(wire).toContain('"type":"layer_visibility_change"');
    expect(wire).toContain('"visible":true');
    const back = JSON.parse(wire) as Envelope<LayerVisibilityChangePayload>;
    expect(back.payload.layerId).toBe(5);
    expect(back.payload.visible).toBe(true);
    expect(back.payload.theme).toBe('default');
  });

  it('serializes layer_visibility_change visible=false without theme (F402)', () => {
    const original: Envelope<LayerVisibilityChangePayload> = {
      type: 'layer_visibility_change',
      payload: { layerId: 7, visible: false }
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<LayerVisibilityChangePayload>;
    expect(back.payload.layerId).toBe(7);
    expect(back.payload.visible).toBe(false);
    expect(back.payload.theme).toBeUndefined();
  });
});
