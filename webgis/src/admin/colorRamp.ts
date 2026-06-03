// D'204 (WD'2): カラーランプ UI ロジック
// 数値属性の breaks 計算 + パレット色配列生成 + プレビュー描画
// API: GET /api/admin/layers/{id}/attributes/{field}/stats?bins=N&method=quantile|equal

import { getCurrentAccessToken } from '../api/client';

export interface AttributeStatsResponse {
  field: string;
  method: string;
  bins: number;
  breaks: number[];
  min: number;
  max: number;
  count: number;
}

export interface ColorRamp {
  field: string;
  breaks: number[];
  colors: string[];
}

// Phase D' MVP パレット: 5 色固定 (bins != 5 の場合は線形補間)
const PALETTES: Record<string, string[]> = {
  Viridis: ['#440154', '#3b528b', '#21918c', '#5ec962', '#fde725'],
  RdYlGn: ['#d73027', '#fc8d59', '#fee08b', '#91cf60', '#1a9850'],
  YlOrRd: ['#ffffcc', '#fed976', '#fd8d3c', '#e31a1c', '#800026']
};

export function generatePaletteColors(paletteName: string, n: number): string[] {
  const base = PALETTES[paletteName] ?? PALETTES.Viridis;
  if (n === base.length) return [...base];
  const out: string[] = [];
  for (let i = 0; i < n; i++) {
    const t = i / (n - 1);
    const srcIdx = t * (base.length - 1);
    const i0 = Math.floor(srcIdx);
    const i1 = Math.min(i0 + 1, base.length - 1);
    const tFrac = srcIdx - i0;
    out.push(interpolateHex(base[i0], base[i1], tFrac));
  }
  return out;
}

function interpolateHex(a: string, b: string, t: number): string {
  const ai = parseInt(a.slice(1), 16);
  const bi = parseInt(b.slice(1), 16);
  const ar = (ai >> 16) & 0xff, ag = (ai >> 8) & 0xff, ab = ai & 0xff;
  const br = (bi >> 16) & 0xff, bg = (bi >> 8) & 0xff, bb = bi & 0xff;
  const r = Math.round(ar + (br - ar) * t);
  const g = Math.round(ag + (bg - ag) * t);
  const bch = Math.round(ab + (bb - ab) * t);
  return `#${((r << 16) | (g << 8) | bch).toString(16).padStart(6, '0')}`;
}

export async function fetchAttributeStats(
  layerId: number,
  field: string,
  bins: number,
  method: 'quantile' | 'equal'
): Promise<AttributeStatsResponse> {
  const token = getCurrentAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const url = `/api/admin/layers/${layerId}/attributes/${encodeURIComponent(field)}/stats?bins=${bins}&method=${method}`;
  const res = await fetch(url, { headers });
  if (!res.ok) throw new Error(`stats fetch failed: ${res.status}`);
  return res.json();
}

export async function generateColorRamp(
  layerId: number,
  field: string,
  bins: number,
  method: 'quantile' | 'equal',
  palette: string
): Promise<{ ramp: ColorRamp; stats: AttributeStatsResponse }> {
  const stats = await fetchAttributeStats(layerId, field, bins, method);
  // breaks の数は bins (最後の break は max)、colors はちょうど bins 個
  // SldXmlBuilder.AppendColorRampRules では colors.Length が bin 数、breaks.Length = bins-1 想定
  // ここでは API レスポンスの末尾を削って bin 境界 (bins-1 個) を作成
  const breaksForSld = stats.breaks.slice(0, Math.max(0, stats.breaks.length - 1));
  const colors = generatePaletteColors(palette, bins);
  return { ramp: { field, breaks: breaksForSld, colors }, stats };
}

export function renderRampPreview(container: HTMLElement, colors: string[]): void {
  container.innerHTML = '';
  for (const c of colors) {
    const div = document.createElement('div');
    div.style.background = c;
    container.appendChild(div);
  }
}
