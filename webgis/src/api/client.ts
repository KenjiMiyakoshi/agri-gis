import type { FeatureCollectionDto, LayerDto } from './types';

// 0402 で X-Actor / X-Request-Id ヘッダ付与や ProblemDetails 解釈を本格化する。
// 本ファイルは骨子のみ。

const API_BASE = '/api';

export async function fetchLayers(): Promise<LayerDto[]> {
  const res = await fetch(`${API_BASE}/layers`);
  if (!res.ok) {
    throw new Error(`fetchLayers failed: ${res.status}`);
  }
  return (await res.json()) as LayerDto[];
}

export async function fetchFeatures(layerId: number): Promise<FeatureCollectionDto> {
  const res = await fetch(`${API_BASE}/features?layerId=${layerId}`);
  if (!res.ok) {
    throw new Error(`fetchFeatures failed: ${res.status}`);
  }
  return await res.json();
}
