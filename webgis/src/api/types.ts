// API DTO の TypeScript ミラー。API 側 record (camelCase) と命名完全一致。
// 案 B' の方針：型ドリブンで WebGIS / WinForms が同じ仕様で API を消費する。

export interface SchemaFieldDto {
  key: string;
  type: string;                  // 'string' | 'number' | 'integer' | 'boolean' | 'date' | 'enum'
  required: boolean;
  label?: string;
}

export interface LayerSchemaDto {
  fields: SchemaFieldDto[];
}

export interface LayerDto {
  layerId: number;
  layerName: string;
  layerType: string;
  ownerOrgId: number | null;
  isShared: boolean;
  createdAt: string;             // ISO 8601
  schemaVersion: number;
  schema: LayerSchemaDto;
  styleVersion: number;          // D'101 (WD'1): cache busting で URL の ?sv= に使う
}

export interface LayerSchemaResponseDto {
  layerId: number;
  schemaVersion: number;
  schema: LayerSchemaDto;
}

export interface FeaturePropertiesDto {
  featureId: number;
  layerId: number;
  entityId: string;
  version: number;
  validFrom: string;             // YYYY-MM-DD
  validTo: string;               // YYYY-MM-DD
  attributesSchemaVersion: number;
  createdBy: string;
  updatedBy: string;
  createdAt: string;             // ISO 8601
  updatedAt: string;             // ISO 8601
  attributes: Record<string, unknown>;
}

export interface FeatureDto {
  type: 'Feature';
  geometry: unknown;             // GeoJSON geometry
  properties: FeaturePropertiesDto;
}

export interface CrsDto {
  type: string;                  // 'name'
  properties: { name: string };  // 'EPSG:4326'
}

export interface FeatureCollectionDto {
  type: 'FeatureCollection';
  crs: CrsDto;
  features: FeatureDto[];
}

export interface FeatureHistoryDto {
  historyId: number;
  featureId: number;
  layerId: number;
  entityId: string;
  geometry: unknown;
  attributes: Record<string, unknown>;
  attributesSchemaVersion: number;
  validFrom: string;
  validTo: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
  updatedBy: string;
  archivedAt: string;
  archivedBy: string;
  archivedReason: string;        // 'update' | 'delete'
}

export interface CreateFeatureRequestDto {
  layerId: number;
  geometry: unknown;             // GeoJSON 4326
  attributes: Record<string, unknown>;
}

export interface UpdateFeatureRequestDto {
  geometry?: unknown | null;     // null/未指定で据え置き
  attributes?: Record<string, unknown> | null;
}

export interface UpdateSchemaRequestDto {
  schema: LayerSchemaDto;
}

export interface UpdateSchemaResponseDto {
  layerId: number;
  schemaVersion: number;
}

export interface AttributeErrorDto {
  attributeKey: string;
  code: string;
  message: string;
}

// ASP.NET Core の ProblemDetails 形。errors は extensions 配下、
// または top-level に乗ることがあるため両方を見るヘルパで吸収する。
export interface ProblemDetailsDto {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  requestId?: string;
  errors?: AttributeErrorDto[];
  extensions?: {
    requestId?: string;
    errors?: AttributeErrorDto[];
  };
}
