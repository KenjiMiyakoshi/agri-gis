# 0402: WebGIS API クライアントと型定義 (DTO 命名一致)

| 項目 | 値 |
|---|---|
| Phase | WebGIS |
| Estimate | 0.5d |
| Depends on | 0401, 0202 |
| Blocks | なし |

## 概要
`api/types.ts` に API の record DTO と命名一致する TypeScript 型を定義し、`api/client.ts` で取得関数を整える。

## 背景・目的
案 B' は API 側で record DTO を持つので、Web 側もそれと **キー名 (camelCase) を完全一致** させる。型ドリブンに WebGIS / WinForms が同じ仕様で API を消費する。

## スコープ
### 含む
- `api/types.ts` に以下を export
  - `LayerDto`, `LayerSchemaDto`, `SchemaFieldDto`
  - `FeatureDto`, `FeatureCollectionDto`, `FeaturePropertiesDto`
  - `CreateFeatureRequestDto`, `UpdateFeatureRequestDto`
  - `ProblemDetailsDto` (status, title, type, extensions: { requestId, errors? })
  - `AttributeErrorDto`
- `api/client.ts`
  - `getLayers()`
  - `getFeatures(layerId, asOf?)`
  - `getFeature(entityId, asOf?)`
  - `postFeature(req, actor)`
  - `patchFeature(entityId, req, actor, ifMatchVersion)`
  - `deleteFeature(entityId, actor)`
  - 例外: 4xx/5xx のとき `ProblemDetailsDto` を投げる
- `controllers/layer.ts` を新 client に置換

### 含まない
- bridge メッセージ (0403)
- 編集 UI（本サイクル外、WinForms 側で実装）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `getLayers().then(ls => ls[0].schemaVersion)` の型推論が通る
- [ ] エラーレスポンス時に `ProblemDetailsDto` 形の例外が throw される
- [ ] `getFeatures(1, '2026-01-01')` の URL が `?layerId=1&asOf=2026-01-01`
- [ ] `tsc --noEmit` が通る

## 影響ファイル
- `D:\proj\agri-gis\webgis\src\api\types.ts` (本格実装)
- `D:\proj\agri-gis\webgis\src\api\client.ts` (本格実装)
- `D:\proj\agri-gis\webgis\src\controllers\layer.ts` (移行)

## 実装ノート
```ts
// api/types.ts
export interface LayerSchemaDto { fields: SchemaFieldDto[]; }
export interface SchemaFieldDto { key: string; type: string; required: boolean; label?: string; }

export interface LayerDto {
  layerId: number;
  layerName: string;
  layerType: string;
  ownerOrgId: number | null;
  isShared: boolean;
  createdAt: string; // ISO 8601
  schemaVersion: number;
  schema: LayerSchemaDto;
}

export interface FeaturePropertiesDto {
  featureId: number;
  layerId: number;
  entityId: string;
  version: number;
  validFrom: string; // YYYY-MM-DD
  validTo: string;
  attributesSchemaVersion: number;
  createdBy: string;
  updatedBy: string;
  createdAt: string;
  updatedAt: string;
  [k: string]: unknown; // 属性
}
export interface FeatureDto { type: 'Feature'; geometry: unknown; properties: FeaturePropertiesDto; }
export interface FeatureCollectionDto { type: 'FeatureCollection'; crs: { type: string; properties: { name: string } }; features: FeatureDto[]; }

export interface AttributeErrorDto { attributeKey: string; code: string; message: string; }
export interface ProblemDetailsDto {
  type?: string; title?: string; status?: number; detail?: string;
  extensions?: { requestId?: string; errors?: AttributeErrorDto[]; };
}
```

```ts
// api/client.ts
const BASE = '/api';
async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const pd = await res.json().catch(() => ({})) as ProblemDetailsDto;
    throw Object.assign(new Error(pd.title ?? res.statusText), { problem: pd, status: res.status });
  }
  return res.json() as Promise<T>;
}
export const getLayers   = () => fetch(`${BASE}/layers`).then(handle<LayerDto[]>);
export const getFeatures = (layerId: number, asOf?: string) => {
  const u = new URL(`${BASE}/features`, location.origin);
  u.searchParams.set('layerId', String(layerId));
  if (asOf) u.searchParams.set('asOf', asOf);
  return fetch(u.toString().replace(location.origin, '')).then(handle<FeatureCollectionDto>);
};
// ... 他
```

注意点:
- ProblemDetails の `extensions` は ASP.NET Core のシリアライザ仕様により実際は `top-level` にフラットに乗ることがある。本イシューでは `extensions` フィールド + フラット top-level の両対応のヘルパを書く

## テスト観点
- 0404: ProblemDetails パーサのケースを Vitest（任意、最小）
