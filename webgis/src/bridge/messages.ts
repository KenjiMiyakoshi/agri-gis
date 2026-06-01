// WebView2 ホスト (WinForms) と WebGIS の双方向メッセージ規約。
// envelope: { type, payload, requestId? }

export type WebToHostType = 'feature_clicked' | 'map_ready';
export type HostToWebType = 'layer_select' | 'features_reload' | 'feature_highlight';
export type MessageType = WebToHostType | HostToWebType;

export interface Envelope<P = unknown> {
  type: MessageType;
  payload: P;
  requestId?: string;
}

// --- Web → Host payloads ---

export interface FeatureClickedPayload {
  entityId: string;
  layerId: number;
  featureId?: number;
}

// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface MapReadyPayload {}

// --- Host → Web payloads ---

export interface LayerSelectPayload {
  layerId: number;
  asOf?: string;
}

export interface FeaturesReloadPayload {
  layerId: number;
  asOf?: string;
}

export interface FeatureHighlightPayload {
  entityId: string;
}

// 将来追加予定: feature_edit_geometry / view_set_rotation など
