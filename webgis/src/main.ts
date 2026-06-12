import { createMap } from './map/mapInit';
import { loadFeatures, wireLayerSelect, addLayer, removeLayer } from './controllers/layer';
import { wireRotation } from './controllers/rotation';
import { wireSelection } from './controllers/selection';
import { onMessage, sendToHost } from './bridge/webviewBridge';
import type {
  AsOfChangePayload,
  AuthTokenPayload,
  FeaturesReloadPayload,
  LayerSelectPayload,
  LayerVisibilityChangePayload
} from './bridge/messages';
import { changeAsOf } from './controllers/layer';
import { setAccessToken, fetchLayers } from './api/client';
// F'203 (Phase F' WF'2): per-layer subscribe を単一 subscribeLayers に統合
import { subscribeLayers } from './controllers/eventStream';
import { getVisibleLayerIds } from './controllers/layer';

const ctx = createMap('map');
wireRotation(ctx);
wireSelection(ctx);
void wireLayerSelect(ctx);

// Host → Web: auth_token / layer_select / layer_visibility_change / features_reload を受ける
// D303 (WD3): layer_select で theme 引数も受領
// F402 (Phase F WF4): layer_visibility_change を追加 (WinForms CheckedListBox の ON/OFF を反映)
onMessage((msg) => {
  if (msg.type === 'auth_token') {
    const p = msg.payload as AuthTokenPayload;
    setAccessToken(p.accessToken);
  } else if (msg.type === 'layer_select') {
    const p = msg.payload as LayerSelectPayload;
    // E401 (WE4): layer_select.asOf もあれば反映 (現在の asOf を維持する場合は省略)
    if (p.asOf !== undefined) ctx.currentAsOf = p.asOf ?? null;
    void loadFeatures(ctx, p.layerId, p.theme ?? 'default', ctx.currentAsOf);
  } else if (msg.type === 'layer_visibility_change') {
    const p = msg.payload as LayerVisibilityChangePayload;
    void handleLayerVisibilityChange(p);
  } else if (msg.type === 'features_reload') {
    const p = msg.payload as FeaturesReloadPayload;
    void loadFeatures(ctx, p.layerId, ctx.currentTheme, ctx.currentAsOf);
  } else if (msg.type === 'asof_change') {
    // E401 (WE4): WinForms の DateTimePicker 値変更通知
    const p = msg.payload as AsOfChangePayload;
    void changeAsOf(ctx, p.asOf ?? null);
  }
});

// F402 (Phase F WF4): visible=true で addLayer + SSE 購読開始、false で removeLayer + 解除
// F'203 (Phase F' WF'2): per-layer subscribe を廃止、現在の layerStack 全体で subscribeLayers を呼ぶ
async function handleLayerVisibilityChange(p: LayerVisibilityChangePayload): Promise<void> {
  if (p.visible) {
    let sv: number | null = null;
    try {
      const layers = await fetchLayers(ctx.currentAsOf ?? undefined);
      sv = layers.find(l => l.layerId === p.layerId)?.styleVersion ?? null;
    } catch (e) {
      console.warn('[layer_visibility_change] fetchLayers failed', e);
    }
    addLayer(ctx, p.layerId, p.theme ?? 'default', ctx.currentAsOf, sv);
  } else {
    removeLayer(ctx, p.layerId);
  }
  // F'203: 表示中 layer 集合が変わったので SSE 購読集合を更新
  subscribeLayers(ctx, getVisibleLayerIds(ctx));
}

// 起動完了を Host に通知
sendToHost({ type: 'map_ready', payload: {} });
