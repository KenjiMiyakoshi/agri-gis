import type {
  CreateFeatureRequestDto,
  FeatureCollectionDto,
  FeatureDto,
  FeatureHistoryDto,
  LayerDto,
  LayerSchemaResponseDto,
  ProblemDetailsDto,
  UpdateFeatureRequestDto
} from './types';

const BASE = '/api';

// WinForms (ホスト) から bridge 経由で渡される JWT。setAccessToken で更新。
// 未設定の間は Authorization ヘッダを付けない (一部 anonymous エンドポイント用)。
let _accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  _accessToken = token;
}

// 認証ヘッダを既存ヘッダ群にマージ
function withAuth(headers?: HeadersInit): HeadersInit {
  if (!_accessToken) return headers ?? {};
  const merged: Record<string, string> = {};
  if (headers) {
    if (headers instanceof Headers) {
      headers.forEach((v, k) => { merged[k] = v; });
    } else if (Array.isArray(headers)) {
      for (const [k, v] of headers) merged[k] = v;
    } else {
      Object.assign(merged, headers as Record<string, string>);
    }
  }
  merged['Authorization'] = `Bearer ${_accessToken}`;
  return merged;
}

// fetch ラッパ: 認証ヘッダを自動付与
function authFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  return fetch(input, { ...(init ?? {}), headers: withAuth(init?.headers) });
}

// 4xx/5xx は ProblemDetails (application/problem+json) を期待してパースし、
// ApiError として throw する。呼び出し側は .problem で詳細にアクセス。
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetailsDto;
  constructor(status: number, problem: ProblemDetailsDto) {
    super(problem.title ?? `HTTP ${status}`);
    this.status = status;
    this.problem = problem;
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let pd: ProblemDetailsDto = {};
    try {
      pd = (await res.json()) as ProblemDetailsDto;
    } catch {
      pd = { status: res.status, title: res.statusText };
    }
    throw new ApiError(res.status, pd);
  }
  if (res.status === 204) {
    return undefined as unknown as T;
  }
  return (await res.json()) as T;
}

function jsonHeaders(actor?: string, ifMatch?: number, requestId?: string): HeadersInit {
  const h: Record<string, string> = { 'Content-Type': 'application/json' };
  if (actor) h['X-Actor'] = actor;
  if (ifMatch !== undefined) h['If-Match'] = String(ifMatch);
  if (requestId) h['X-Request-Id'] = requestId;
  return h;
}

// --- Layer ---

export async function getLayers(): Promise<LayerDto[]> {
  return handle<LayerDto[]>(await authFetch(`${BASE}/layers`));
}

export async function getLayerSchema(layerId: number): Promise<LayerSchemaResponseDto> {
  return handle<LayerSchemaResponseDto>(await authFetch(`${BASE}/layers/${layerId}/schema`));
}

// --- Features (read) ---

export async function getFeatures(layerId: number, asOf?: string): Promise<FeatureCollectionDto> {
  const params = new URLSearchParams({ layerId: String(layerId) });
  if (asOf) params.set('asOf', asOf);
  return handle<FeatureCollectionDto>(await authFetch(`${BASE}/features?${params.toString()}`));
}

export async function getFeature(entityId: string, asOf?: string): Promise<FeatureDto> {
  const qs = asOf ? `?asOf=${encodeURIComponent(asOf)}` : '';
  return handle<FeatureDto>(await authFetch(`${BASE}/features/${entityId}${qs}`));
}

export async function getFeatureHistory(entityId: string): Promise<FeatureHistoryDto[]> {
  return handle<FeatureHistoryDto[]>(await authFetch(`${BASE}/features/${entityId}/history`));
}

// --- Features (write) ---

export interface PostFeatureResponse {
  featureId: number;
  entityId: string;
  version: number;
  attributesSchemaVersion: number;
}

export async function postFeature(
  req: CreateFeatureRequestDto,
  actor: string,
  requestId?: string
): Promise<PostFeatureResponse> {
  const res = await authFetch(`${BASE}/features`, {
    method: 'POST',
    headers: jsonHeaders(actor, undefined, requestId),
    body: JSON.stringify(req)
  });
  return handle<PostFeatureResponse>(res);
}

export interface PatchFeatureResponse {
  entityId: string;
  version: number;
}

export async function patchFeature(
  entityId: string,
  req: UpdateFeatureRequestDto,
  actor: string,
  ifMatchVersion: number,
  requestId?: string
): Promise<PatchFeatureResponse> {
  const res = await authFetch(`${BASE}/features/${entityId}`, {
    method: 'PATCH',
    headers: jsonHeaders(actor, ifMatchVersion, requestId),
    body: JSON.stringify(req)
  });
  return handle<PatchFeatureResponse>(res);
}

export async function deleteFeature(
  entityId: string,
  actor: string,
  requestId?: string
): Promise<void> {
  const res = await authFetch(`${BASE}/features/${entityId}`, {
    method: 'DELETE',
    headers: jsonHeaders(actor, undefined, requestId)
  });
  await handle<void>(res);
}

// --- 互換エイリアス（既存呼び出しを壊さないため） ---

export const fetchLayers = getLayers;
export const fetchFeatures = (layerId: number) => getFeatures(layerId);
