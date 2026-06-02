import XYZ from 'ol/source/XYZ';
import type { MapContext } from '../map/mapInit';
import type { FeaturesSelectedPayload, SelectionOverlayReadyPayload, ThemeChangePayload } from '../bridge/messages';
import { sendToHost, onMessage } from '../bridge/webviewBridge';
import { createSelection, getCurrentAccessToken } from '../api/client';
import { changeTheme } from './layer';

// D302 (WD3): 選択 2 段パイプライン
//   1) クリック → WMS GetFeatureInfo で entity_id 取得 (現実装は Phase D MVP として未実装)
//   2) POST /api/selection で sid 発行
//   3) selectionLayer の source を /tiles/selection/{sid}/{z}/{x}/{y}.png に差替
//   4) Host に features_selected + selection_overlay_ready を envelope 送信
//
// 注: 1) の WMS GetFeatureInfo 経路は Phase D MVP では「クリック座標を Host に渡し
//     Host (WinForms) が API に問い合わせる」二段でも実現可能。本 PR は selection の
//     パイプライン形だけ書き、entity_id 取得は将来 Wave で精緻化する。

export function wireSelection(ctx: MapContext): void {
  ctx.map.on('singleclick', async (evt) => {
    // 暫定: クリック座標 (EPSG:3857) を Host に通知する経路は廃止
    // 代わりに、entity_id 取得を将来 Wave で WMS GetFeatureInfo に差し替える前提で、
    // 現状は何も送らない (Phase D MVP)。
    void evt;
  });

  onMessage((msg) => {
    if (msg.type === 'theme_change') {
      const p = msg.payload as ThemeChangePayload;
      // theme は currentLayerId と一致しなくても受け入れる (Host が両方更新する想定)
      changeTheme(ctx, p.theme);
      return;
    }
    if (msg.type === 'feature_highlight') {
      // 後方互換: 単一 entity ハイライト要求を Phase D selection 経路に翻訳
      void handleFeatureHighlight(ctx, msg.payload as { entityId: string });
    }
  });
}

async function handleFeatureHighlight(ctx: MapContext, payload: { entityId: string }): Promise<void> {
  try {
    const { sid, count } = await createSelection({ entityIds: [payload.entityId] });
    setSelectionOverlay(ctx, sid);
    const fsel: FeaturesSelectedPayload = {
      entityIds: [payload.entityId],
      sid,
      layerId: ctx.currentLayerId ?? undefined
    };
    sendToHost({ type: 'features_selected', payload: fsel });
    const ready: SelectionOverlayReadyPayload = { sid, count };
    sendToHost({ type: 'selection_overlay_ready', payload: ready });
  } catch (e) {
    console.error('[selection] handleFeatureHighlight failed', e);
  }
}

function setSelectionOverlay(ctx: MapContext, sid: string): void {
  const url = `/tiles/selection/${sid}/{z}/{x}/{y}.png`;
  const source = new XYZ({
    url,
    tileLoadFunction: (tile, src) => {
      const image = (tile as unknown as { getImage(): HTMLImageElement }).getImage();
      const token = getCurrentAccessToken();
      if (!token) {
        image.src = src;
        return;
      }
      fetch(src, { headers: { Authorization: `Bearer ${token}` } })
        .then((r) => r.blob())
        .then((blob) => {
          const obj = URL.createObjectURL(blob);
          image.src = obj;
          image.onload = () => URL.revokeObjectURL(obj);
        })
        .catch((e) => console.error('[selection tile]', e));
    },
    crossOrigin: 'anonymous'
  });
  ctx.selectionLayer.setSource(source);
}
