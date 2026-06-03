import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { getLayers, getLayerExtent, getFeaturesAt, setAccessToken } from '../client';

// E502 (WE5): asOf 引数が URL に正しく載ることを検証 (tileLayer + selection の経路)
describe('client.ts asOf wiring', () => {
  let fetchSpy: any;

  beforeEach(() => {
    setAccessToken('test-token');
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ layers: [], hits: [], layerId: 1, count: 0, extent3857: null }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );
  });

  afterEach(() => {
    fetchSpy.mockRestore();
    setAccessToken(null);
  });

  it('getLayers without asOf does not append ?asOf=', async () => {
    await getLayers();
    const callUrl = (fetchSpy.mock.calls[0][0] as string);
    expect(callUrl).toBe('/api/layers');
  });

  it('getLayers with asOf appends ?asOf=YYYY-MM-DD', async () => {
    await getLayers('2025-01-01');
    const callUrl = (fetchSpy.mock.calls[0][0] as string);
    expect(callUrl).toBe('/api/layers?asOf=2025-01-01');
  });

  it('getLayerExtent with asOf appends ?asOf=', async () => {
    await getLayerExtent(7, '2025-04-15');
    const callUrl = (fetchSpy.mock.calls[0][0] as string);
    expect(callUrl).toBe('/api/layers/7/extent?asOf=2025-04-15');
  });

  it('getFeaturesAt with asOf appends asOf in query string', async () => {
    await getFeaturesAt(7, 100, 200, 50, '2025-06-30');
    const callUrl = (fetchSpy.mock.calls[0][0] as string);
    expect(callUrl).toContain('asOf=2025-06-30');
    expect(callUrl).toContain('x=100');
    expect(callUrl).toContain('y=200');
    expect(callUrl).toContain('tolerance=50');
  });

  it('getFeaturesAt without asOf omits asOf param', async () => {
    await getFeaturesAt(7, 100, 200, 50);
    const callUrl = (fetchSpy.mock.calls[0][0] as string);
    expect(callUrl).not.toContain('asOf');
  });
});
