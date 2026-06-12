import XYZ from 'ol/source/XYZ';
import type { MapContext } from '../map/mapInit';
import type { FeaturesSelectedPayload, SelectionOverlayReadyPayload, ThemeChangePayload } from '../bridge/messages';
import { sendToHost, onMessage } from '../bridge/webviewBridge';
import { createSelection, getCurrentAccessToken, getFeaturesAt } from '../api/client';
import { changeTheme, getVisibleLayerIds } from './layer';

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
  // hotfix 3件目: singleclick → API /layers/{id}/at で近傍 feature の entity_id 取得
  // → POST /api/selection で sid 発行 → selection overlay 表示
  // → WinForms に features_selected envelope 通知 (属性表示は WinForms 側)
  //
  // F403 (Phase F WF4): 全 visible layer に対し getFeaturesAt を並列実行し、
  //   最上位 (最後に addLayer された) layer の最近接 hit を採用する。
  //   layerStack の挿入順序 = レンダリング順 (後追加が上) を信頼する。
  ctx.map.on('singleclick', async (evt) => {
    const visibleIds = getVisibleLayerIds(ctx);
    if (visibleIds.length === 0) return;
    const [x, y] = evt.coordinate;  // EPSG:3857
    // 画面解像度に応じた tolerance (z=15 で約 100m、z=10 で約 3000m)
    const resolution = ctx.view.getResolution() ?? 1;
    const tolerance = resolution * 10;  // 10 pixel 相当
    try {
      // F403: 全 visible layer に並列に問い合わせ、エラーは無視
      const results = await Promise.all(visibleIds.map(async (lid) => {
        try {
          return { layerId: lid, hit: await getFeaturesAt(lid, x, y, tolerance, ctx.currentAsOf ?? undefined) };
        } catch (e) {
          console.warn('[selection] getFeaturesAt failed', lid, e);
          return null;
        }
      }));
      // 最上位 hit を採用 (visibleIds 末尾から探索)
      let pick: { layerId: number; entityIds: string[] } | null = null;
      for (let i = visibleIds.length - 1; i >= 0; i--) {
        const r = results.find(rr => rr?.layerId === visibleIds[i]);
        if (r && r.hit.hits.length > 0) {
          pick = { layerId: r.layerId, entityIds: r.hit.hits.map(h => h.entityId) };
          break;
        }
      }
      if (!pick) return;
      const sel = await createSelection({ entityIds: pick.entityIds });
      setSelectionOverlay(ctx, sel.sid);
      const fsel: FeaturesSelectedPayload = {
        entityIds: pick.entityIds,
        sid: sel.sid,
        layerId: pick.layerId
      };
      sendToHost({ type: 'features_selected', payload: fsel });
      const ready: SelectionOverlayReadyPayload = { sid: sel.sid, count: sel.count };
      sendToHost({ type: 'selection_overlay_ready', payload: ready });
    } catch (e) {
      console.error('[selection] singleclick failed', e);
    }
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
