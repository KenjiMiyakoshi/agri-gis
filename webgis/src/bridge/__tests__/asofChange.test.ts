import { describe, it, expect } from 'vitest';
import type { Envelope, AsOfChangePayload } from '../messages';

// E502 (WE5): asof_change envelope の round trip + null 表現
describe('asof_change envelope', () => {
  it('serializes asOf=2025-04-15', () => {
    const original: Envelope<AsOfChangePayload> = {
      type: 'asof_change',
      payload: { asOf: '2025-04-15' }
    };
    const wire = JSON.stringify(original);
    expect(wire).toContain('"type":"asof_change"');
    expect(wire).toContain('"asOf":"2025-04-15"');
    const back = JSON.parse(wire) as Envelope<AsOfChangePayload>;
    expect(back.payload.asOf).toBe('2025-04-15');
  });

  it('serializes asOf=null (現在モード)', () => {
    const original: Envelope<AsOfChangePayload> = {
      type: 'asof_change',
      payload: { asOf: null }
    };
    const wire = JSON.stringify(original);
    const back = JSON.parse(wire) as Envelope<AsOfChangePayload>;
    expect(back.payload.asOf).toBeNull();
  });
});
