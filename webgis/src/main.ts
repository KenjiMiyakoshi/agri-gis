import { createMap } from './map/mapInit';
import { loadFeatures, wireLayerSelect } from './controllers/layer';
import { wireRotation } from './controllers/rotation';
import { wireSelection } from './controllers/selection';
import { onMessage, sendToHost } from './bridge/webviewBridge';
import type { FeaturesReloadPayload, LayerSelectPayload } from './bridge/messages';

const ctx = createMap('map');
wireRotation(ctx);
wireSelection(ctx);
void wireLayerSelect(ctx);

// Host → Web: layer_select / features_reload を受けてレイヤを再ロード
onMessage((msg) => {
  if (msg.type === 'layer_select') {
    const p = msg.payload as LayerSelectPayload;
    void loadFeatures(ctx, p.layerId);
  } else if (msg.type === 'features_reload') {
    const p = msg.payload as FeaturesReloadPayload;
    void loadFeatures(ctx, p.layerId);
  }
});

// 起動完了を Host に通知
sendToHost({ type: 'map_ready', payload: {} });
