import type { MapContext } from '../map/mapInit';
import type { FeatureClickedPayload, FeatureHighlightPayload } from '../bridge/messages';
import { sendToHost, onMessage } from '../bridge/webviewBridge';

// 地図クリック → 最初のヒット feature を Host に通知。
// Host からの feature_highlight を受けて該当 feature を選択状態にする。
export function wireSelection(ctx: MapContext): void {
  ctx.map.on('singleclick', (evt) => {
    let payload: FeatureClickedPayload | null = null;
    ctx.map.forEachFeatureAtPixel(evt.pixel, (feat) => {
      const props = feat.getProperties() as Record<string, unknown>;
      const entityId = props['entityId'] as string | undefined;
      const layerId = props['layerId'] as number | undefined;
      const featureId = props['featureId'] as number | undefined;
      if (entityId && layerId !== undefined) {
        payload = { entityId, layerId, featureId };
        return true;
      }
      return false;
    });
    if (payload) {
      sendToHost({ type: 'feature_clicked', payload });
    }
  });

  onMessage((msg) => {
    if (msg.type !== 'feature_highlight') return;
    const p = msg.payload as FeatureHighlightPayload;
    // 現状はログのみ（実 UI ハイライトは将来の Style 切替で対応）
    console.info('[selection] feature_highlight', p.entityId);
  });
}
