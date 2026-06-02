import { createMap } from './map/mapInit';
import { loadFeatures, wireLayerSelect } from './controllers/layer';
import { wireRotation } from './controllers/rotation';
import { wireSelection } from './controllers/selection';
import { onMessage, sendToHost } from './bridge/webviewBridge';
import type {
  AuthTokenPayload,
  FeaturesReloadPayload,
  LayerSelectPayload
} from './bridge/messages';
import { setAccessToken } from './api/client';

const ctx = createMap('map');
wireRotation(ctx);
wireSelection(ctx);
void wireLayerSelect(ctx);

// Host → Web: auth_token / layer_select / features_reload を受ける
onMessage((msg) => {
  if (msg.type === 'auth_token') {
    const p = msg.payload as AuthTokenPayload;
    setAccessToken(p.accessToken);
  } else if (msg.type === 'layer_select') {
    const p = msg.payload as LayerSelectPayload;
    void loadFeatures(ctx, p.layerId);
  } else if (msg.type === 'features_reload') {
    const p = msg.payload as FeaturesReloadPayload;
    void loadFeatures(ctx, p.layerId);
  }
});

// 起動完了を Host に通知
sendToHost({ type: 'map_ready', payload: {} });
