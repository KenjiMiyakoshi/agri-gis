// D'402 (Phase D' post-merge fix): colorRamp パレット計算 unit テスト
import { describe, it, expect } from 'vitest';
import { generatePaletteColors } from '../colorRamp';

describe('colorRamp.generatePaletteColors', () => {
  it('returns Viridis 5 colors when n=5 (no interpolation)', () => {
    const colors = generatePaletteColors('Viridis', 5);
    expect(colors).toHaveLength(5);
    expect(colors[0].toLowerCase()).toBe('#440154');
    expect(colors[4].toLowerCase()).toBe('#fde725');
  });

  it('interpolates RdYlGn when n=3 (endpoints固定)', () => {
    const colors = generatePaletteColors('RdYlGn', 3);
    expect(colors).toHaveLength(3);
    expect(colors[0].toLowerCase()).toBe('#d73027');
    expect(colors[2].toLowerCase()).toBe('#1a9850');
  });

  it('interpolates Viridis when n=10 (上下端は固定 + 中間は有効な hex)', () => {
    const colors = generatePaletteColors('Viridis', 10);
    expect(colors).toHaveLength(10);
    expect(colors[0].toLowerCase()).toBe('#440154');
    expect(colors[9].toLowerCase()).toBe('#fde725');
    for (const c of colors) {
      expect(c).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });

  it('falls back to Viridis on unknown palette name', () => {
    const fallback = generatePaletteColors('Unknown', 5);
    const viridis = generatePaletteColors('Viridis', 5);
    expect(fallback).toEqual(viridis);
  });

  it('YlOrRd 5 色は規定パレットを返す', () => {
    const colors = generatePaletteColors('YlOrRd', 5);
    expect(colors).toHaveLength(5);
    expect(colors[0].toLowerCase()).toBe('#ffffcc');
    expect(colors[4].toLowerCase()).toBe('#800026');
  });
});
