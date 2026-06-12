// WebView2 ホスト (WinForms) と WebGIS の双方向メッセージ規約。
// envelope: { type, payload, requestId? }
//
// Phase D D303 (WD3) で `feature_clicked` を廃止し、配列対応の
// `features_selected` + `theme_change` + `selection_overlay_ready` に切替。

export type WebToHostType = 'features_selected' | 'selection_overlay_ready' | 'map_ready';
// F402 (Phase F WF4): 'layer_visibility_change' を追加 (複数 layer 同時 ON/OFF)
export type HostToWebType = 'layer_select' | 'layer_visibility_change' | 'features_reload' | 'feature_highlight' | 'theme_change' | 'asof_change' | 'auth_token';
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

// F402 (Phase F WF4): 複数 layer 同時 ON/OFF 用 envelope。
//   visible=true で addLayer (新規 or 既存表示)、false で removeLayer。
//   1 イベント = 1 layer の状態変更 (バルク化は F' 申し送り)。
export interface LayerVisibilityChangePayload {
  layerId: number;
  visible: boolean;
  // 既定 theme を併送 (admin が将来 layer 単位 theme 設定する想定の拡張ポイント)
  theme?: string;
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

// E401 (WE4): WinForms の DateTimePicker 値変更 → WebGIS の asOf 切替
// asOf=null = 現在 (= valid_to='9999-12-31')
export interface AsOfChangePayload {
  asOf: string | null;
}

// WinForms (ホスト) から API 呼び出し用の JWT を引き渡す (Phase A 動作確認用)
export interface AuthTokenPayload {
  accessToken: string;
}

// 旧 feature_clicked envelope は Phase D で完全廃止
// 互換性破壊: WinForms 側 D401 で features_selected を受領するように更新
