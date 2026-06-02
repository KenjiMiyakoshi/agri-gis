// WebView2 ホスト (WinForms) と WebGIS の双方向メッセージ規約。
// envelope: { type, payload, requestId? }
//
// Phase D D303 (WD3) で `feature_clicked` を廃止し、配列対応の
// `features_selected` + `theme_change` + `selection_overlay_ready` に切替。

export type WebToHostType = 'features_selected' | 'selection_overlay_ready' | 'map_ready';
export type HostToWebType = 'layer_select' | 'features_reload' | 'feature_highlight' | 'theme_change' | 'auth_token';
export type MessageType = WebToHostType | HostToWebType;

export interface Envelope<P = unknown> {
  type: MessageType;
  payload: P;
  requestId?: string;
}

// --- Web → Host payloads ---

// D303 (WD3): feature_clicked → features_selected (配列対応 + sid)
// クリック単数モードでも entityIds は要素 1 件配列で送る
export interface FeaturesSelectedPayload {
  entityIds: string[];
  sid: string;             // POST /api/selection で発行された sid
  layerId?: number;
}

// D303 (WD3): selection overlay の準備完了通知
export interface SelectionOverlayReadyPayload {
  sid: string;
  count: number;
}

// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface MapReadyPayload {}

// --- Host → Web payloads ---

export interface LayerSelectPayload {
  layerId: number;
  // D303 (WD3): theme を併送、未指定で 'default'
  theme?: string;
  asOf?: string;
}

export interface FeaturesReloadPayload {
  layerId: number;
  asOf?: string;
}

export interface FeatureHighlightPayload {
  entityId: string;
}

// D303 (WD3): WinForms 側から theme 切替指示
export interface ThemeChangePayload {
  layerId: number;
  theme: string;
}

// WinForms (ホスト) から API 呼び出し用の JWT を引き渡す (Phase A 動作確認用)
export interface AuthTokenPayload {
  accessToken: string;
}

// 旧 feature_clicked envelope は Phase D で完全廃止
// 互換性破壊: WinForms 側 D401 で features_selected を受領するように更新
