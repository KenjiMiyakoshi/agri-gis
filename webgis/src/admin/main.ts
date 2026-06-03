// D'202+D'203+D'204 (WD'2): admin-style.html のエントリポイント
// JSON エディタ + プレビュー OL map + カラーランプ UI を統合。
// 認証は親 (WinForms) からの bridge 経由を期待するが、ブラウザ単体起動時には
// localStorage に保存された JWT を読み取る簡易フォールバックを行う。

import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import OSM from 'ol/source/OSM';
import { setAccessToken, getCurrentAccessToken, getLayers, getLayerSchema, ApiError } from '../api/client';
import { generateColorRamp, renderRampPreview, generatePaletteColors } from './colorRamp';

interface AdminState {
  layerId: number | null;
  themeName: string;
  styleJson: any;
  styleVersion: number;
}

const state: AdminState = {
  layerId: null,
  themeName: 'default',
  styleJson: { fillColor: '#4CAF50', fillOpacity: 0.5, strokeColor: '#1B5E20', strokeWidth: 1 },
  styleVersion: 1
};

const $ = (id: string) => document.getElementById(id)!;

async function loadJwtFromBridge(): Promise<void> {
  // 親 WinForms から渡された JWT を使うパスは別途実装 (WD'3+ で SSE と統合)。
  // 当面は localStorage か prompt にフォールバック (開発時のみ)。
  const cached = localStorage.getItem('agri_gis_admin_jwt');
  if (cached) {
    setAccessToken(cached.trim());
    return;
  }
  const t = prompt('開発用: JWT を貼り付けてください (POST /api/auth/login で取得)');
  if (t) {
    const trimmed = t.trim();
    setAccessToken(trimmed);
    localStorage.setItem('agri_gis_admin_jwt', trimmed);
  }
}

async function fetchLayersWithRetry(): Promise<void> {
  try {
    const layers = await getLayers();
    const sel = $('admin-layer-select') as HTMLSelectElement;
    sel.innerHTML = '';
    for (const l of layers) {
      const opt = document.createElement('option');
      opt.value = String(l.layerId);
      opt.textContent = `${l.layerId}: ${l.layerName} (${l.layerType})`;
      sel.appendChild(opt);
    }
    if (layers.length > 0) {
      sel.value = String(layers[0].layerId);
      await selectLayer(layers[0].layerId);
    }
  } catch (e: any) {
    if (e instanceof ApiError && e.status === 401) {
      localStorage.removeItem('agri_gis_admin_jwt');
      await loadJwtFromBridge();
      return fetchLayersWithRetry();
    }
    setStatus(`Layer 取得失敗: ${e.message}`);
  }
}

let previewBaseLayer: TileLayer<XYZ> | null = null;

function initPreviewMap(): void {
  previewBaseLayer = new TileLayer<XYZ>({ source: undefined, preload: 1 });
  // Map インスタンスは return せず生成のみ (target で DOM にマウントされれば描画される)
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  new Map({
    target: 'preview-map',
    layers: [new TileLayer({ source: new OSM() }), previewBaseLayer],
    view: new View({ center: [15941563, 5298510], zoom: 14 })
  });
}

function updatePreviewTile(layerId: number, theme: string, styleVersion: number): void {
  if (!previewBaseLayer) return;
  const token = getCurrentAccessToken();
  const url = `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png?sv=${styleVersion}`;
  const source = new XYZ({
    url,
    tileLoadFunction: (tile, src) => {
      const image = (tile as any).getImage();
      if (!token) { image.src = src; return; }
      fetch(src, { headers: { Authorization: `Bearer ${token}` } })
        .then((r) => r.blob())
        .then((blob) => {
          const obj = URL.createObjectURL(blob);
          image.src = obj;
          image.onload = () => URL.revokeObjectURL(obj);
        }).catch((e) => console.error('[preview tile]', e));
    },
    crossOrigin: 'anonymous'
  });
  previewBaseLayer.setSource(source);
}

async function selectLayer(layerId: number): Promise<void> {
  state.layerId = layerId;
  // 既存 SLD/style_json を取得
  const token = getCurrentAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  try {
    const styleRes = await fetch(`/api/admin/layers/${layerId}/style`, { headers });
    if (styleRes.ok) {
      const body = await styleRes.json();
      state.styleJson = body.styleJson ?? body;
      state.styleVersion = body.styleVersion ?? 1;
      ($('json-editor') as HTMLTextAreaElement).value = JSON.stringify(state.styleJson, null, 2);
      ($('sld-editor') as HTMLTextAreaElement).value = body.sldXml ?? '(SLD は保存後に取得可能)';
    }
  } catch (e) {
    console.warn('style fetch failed', e);
  }
  // schema から field 候補を populate
  try {
    const schemaRes = await getLayerSchema(layerId);
    const sel = $('ramp-field') as HTMLSelectElement;
    sel.innerHTML = '<option value="">(選択)</option>';
    for (const f of schemaRes.schema.fields) {
      if (f.type === 'number' || f.type === 'integer') {
        const opt = document.createElement('option');
        opt.value = f.key;
        opt.textContent = `${f.key} (${f.label ?? f.type})`;
        sel.appendChild(opt);
      }
    }
  } catch (e) {
    console.warn('schema fetch failed', e);
  }
  updatePreviewTile(layerId, state.themeName, state.styleVersion);
  setStatus(`Layer ${layerId} 読み込み完了 (styleVersion=${state.styleVersion})`);
  ($('admin-save-btn') as HTMLButtonElement).disabled = false;
}

async function saveStyle(): Promise<void> {
  if (state.layerId === null) return;
  const raw = ($('json-editor') as HTMLTextAreaElement).value;
  let parsed: any;
  try { parsed = JSON.parse(raw); }
  catch (e: any) { setStatus(`JSON parse error: ${e.message}`); return; }
  state.themeName = ($('admin-theme-name') as HTMLInputElement).value || 'default';

  const token = getCurrentAccessToken();
  setStatus('保存中...');
  try {
    const res = await fetch(`/api/admin/layers/${state.layerId}/style`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
      },
      body: JSON.stringify({ themeName: state.themeName, styleJson: parsed })
    });
    if (!res.ok) {
      const txt = await res.text();
      setStatus(`保存失敗: HTTP ${res.status} ${txt.slice(0, 200)}`);
      return;
    }
    const body = await res.json();
    state.styleVersion = body.styleVersion ?? state.styleVersion + 1;
    state.styleJson = parsed;
    setStatus(`保存成功 (styleVersion=${state.styleVersion})`);
    updatePreviewTile(state.layerId, state.themeName, state.styleVersion);
  } catch (e: any) {
    setStatus(`保存失敗: ${e.message}`);
  }
}

function setStatus(msg: string): void {
  $('admin-status').textContent = msg;
}

function wireTabs(): void {
  $('tab-json').addEventListener('click', () => {
    $('tab-json').classList.add('active');
    $('tab-sld').classList.remove('active');
    ($('json-editor') as HTMLElement).style.display = '';
    ($('sld-editor') as HTMLElement).style.display = 'none';
  });
  $('tab-sld').addEventListener('click', () => {
    $('tab-sld').classList.add('active');
    $('tab-json').classList.remove('active');
    ($('sld-editor') as HTMLElement).style.display = '';
    ($('json-editor') as HTMLElement).style.display = 'none';
  });
}

function wireColorRamp(): void {
  const previewEl = $('ramp-preview') as HTMLDivElement;
  // パレットプレビューを即時更新
  const paletteSel = $('ramp-palette') as HTMLSelectElement;
  const binsInput = $('ramp-bins') as HTMLInputElement;
  const update = () => {
    const colors = generatePaletteColors(paletteSel.value, parseInt(binsInput.value, 10) || 5);
    renderRampPreview(previewEl, colors);
  };
  paletteSel.addEventListener('change', update);
  binsInput.addEventListener('input', update);
  update();

  $('ramp-apply').addEventListener('click', async () => {
    const fieldSel = $('ramp-field') as HTMLSelectElement;
    if (!fieldSel.value || state.layerId === null) {
      setStatus('属性を選択してください');
      return;
    }
    const bins = parseInt(binsInput.value, 10) || 5;
    const method = ($('ramp-method') as HTMLSelectElement).value as 'quantile' | 'equal';
    setStatus(`stats 取得中 (${fieldSel.value}, bins=${bins})...`);
    try {
      const { ramp, stats } = await generateColorRamp(state.layerId, fieldSel.value, bins, method, paletteSel.value);
      $('stats-info').textContent = `min=${stats.min.toFixed(2)} max=${stats.max.toFixed(2)} count=${stats.count}`;
      // styleJson に colorRamp プロパティを挿入
      state.styleJson = { ...state.styleJson, colorRamp: ramp };
      ($('json-editor') as HTMLTextAreaElement).value = JSON.stringify(state.styleJson, null, 2);
      setStatus('colorRamp を JSON エディタに反映 → 保存してください');
    } catch (e: any) {
      setStatus(`カラーランプ計算失敗: ${e.message}`);
    }
  });
}

(async () => {
  initPreviewMap();
  wireTabs();
  wireColorRamp();
  await loadJwtFromBridge();
  await fetchLayersWithRetry();
  $('admin-save-btn').addEventListener('click', saveStyle);
  ($('admin-layer-select') as HTMLSelectElement).addEventListener('change', (e) => {
    const id = parseInt((e.target as HTMLSelectElement).value, 10);
    if (!isNaN(id)) selectLayer(id);
  });
})();
