import { createMap } from './map/mapInit';
import { loadFeatures, wireLayerSelect } from './controllers/layer';
import { wireRotation } from './controllers/rotation';
import { wireSelection } from './controllers/selection';
import { onMessage, sendToHost } from './bridge/webviewBridge';
import type {
  AsOfChangePayload,
  AuthTokenPayload,
  FeaturesReloadPayload,
  LayerSelectPayload
} from './bridge/messages';
import { changeAsOf } from './controllers/layer';
import { setAccessToken } from './api/client';

const ctx = createMap('map');
wireRotation(ctx);
wireSelection(ctx);
void wireLayerSelect(ctx);

// Host → Web: auth_token / layer_select / features_reload を受ける
// D303 (WD3): layer_select で theme 引数も受領
onMessage((msg) => {
  if (msg.type === 'auth_token') {
    const p = msg.payload as AuthTokenPayload;
    setAccessToken(p.accessToken);
  } else if (msg.type === 'layer_select') {
    const p = msg.payload as LayerSelectPayload;
    // E401 (WE4): layer_select.asOf もあれば反映 (現在の asOf を維持する場合は省略)
    if (p.asOf !== undefined) ctx.currentAsOf = p.asOf ?? null;
    void loadFeatures(ctx, p.layerId, p.theme ?? 'default', ctx.currentAsOf);
  } else if (msg.type === 'features_reload') {
    const p = msg.payload as FeaturesReloadPayload;
    void loadFeatures(ctx, p.layerId, ctx.currentTheme, ctx.currentAsOf);
  } else if (msg.type === 'asof_change') {
    // E401 (WE4): WinForms の DateTimePicker 値変更通知
    const p = msg.payload as AsOfChangePayload;
    void changeAsOf(ctx, p.asOf ?? null);
  }
});

// 起動完了を Host に通知
sendToHost({ type: 'map_ready', payload: {} });
