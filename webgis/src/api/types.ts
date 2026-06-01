// API DTO の TypeScript 側ミラー。API 側 record と命名を一致させる（camelCase）。
// 本ファイルは骨子のみ。0402 で全 DTO を網羅予定。

export interface LayerDto {
  layerId: number;
  layerName: string;
  layerType: string;
  ownerOrgId: number | null;
  isShared: boolean;
  createdAt: string;             // ISO-8601
  schemaVersion: number;
  schema: LayerSchemaDto;
}

export interface LayerSchemaDto {
  fields: SchemaFieldDto[];
}

export interface SchemaFieldDto {
  key: string;
  type: string;                  // 'string' | 'number' | 'integer' | 'boolean' | 'date' | 'enum'
  required: boolean;
  label?: string;
}

// GeoJSON Feature/Collection は OpenLayers の GeoJSON parser に渡すので
// ここでは厳密に型付けせず、最小限の幅で保持する。
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type FeatureCollectionDto = any;
