# 0403: WebView2 bridge + メッセージ envelope + requestId 重複検知

| 項目 | 値 |
|---|---|
| Phase | WebGIS |
| Estimate | 1d |
| Depends on | 0401 |
| Blocks | 0404, 0504, 0601 |

## 概要
WebView2 ホスト (WinForms) と双方向通信するための bridge を実装する。メッセージは `{ type, payload, requestId? }` envelope、受信側で requestId の重複検知を行う。

## 背景・目的
案 B' の WebGIS ↔ WinForms 通信仕様の中核。WebView2 はメッセージを順序保証しないことがあるので、重複検知は明示的に持つ。

## スコープ
### 含む
- `bridge/messages.ts`: メッセージ型定義
  - Web → Host: `feature_clicked`, `map_ready`
  - Host → Web: `layer_select`, `features_reload`, `feature_highlight`
  - 各タイプの payload インターフェース
  - 共通 envelope: `Envelope<TPayload> = { type: string; payload: TPayload; requestId?: string }`
- `bridge/requestIdRegistry.ts`
  - `Set<string>` + 5 分の TTL (`Map<string, number>`)
  - `markSeen(id): boolean` // false なら重複
  - `purgeExpired()`
- `bridge/webviewBridge.ts`
  - `sendToHost<T>(msg: Envelope<T>): void`
  - `onMessage(handler: (m: Envelope<unknown>) => void): () => void`
  - `window.chrome?.webview` が無ければ dev 時 console.warn のみ
  - 受信時に requestId があれば重複検知し、重複ならスキップ
- 既存 OL のクリックを `feature_clicked` として Host に送る薄い配線（`controllers/selection.ts`）
- map 構築完了時に `map_ready` を送る

### 含まない
- Host → Web の細かい挙動（layer_select 受信時の動きはアプリ仕様）
- Vitest テスト (0404)

## 受け入れ条件 (Acceptance Criteria)
- [ ] WebView2 環境で Host から `layer_select` を投げると、当該レイヤがロードされる
- [ ] dev (ブラウザ) でも `window.chrome?.webview` 不在で例外にならない
- [ ] 同じ requestId のメッセージを 2 回受けると 2 回目はスキップ
- [ ] 5 分以上古い requestId は purge される

## 影響ファイル
- `D:\proj\agri-gis\webgis\src\bridge\messages.ts` (新規)
- `D:\proj\agri-gis\webgis\src\bridge\requestIdRegistry.ts` (新規)
- `D:\proj\agri-gis\webgis\src\bridge\webviewBridge.ts` (新規)
- `D:\proj\agri-gis\webgis\src\controllers\selection.ts` (新規)
- `D:\proj\agri-gis\webgis\src\main.ts` (配線)

## 実装ノート
```ts
// bridge/messages.ts
export type MessageType =
  | 'feature_clicked' | 'map_ready'              // Web -> Host
  | 'layer_select' | 'features_reload' | 'feature_highlight'; // Host -> Web

export interface Envelope<P = unknown> { type: MessageType; payload: P; requestId?: string; }

export interface FeatureClickedPayload { entityId: string; layerId: number; }
export interface MapReadyPayload { /* empty */ }
export interface LayerSelectPayload { layerId: number; }
export interface FeaturesReloadPayload { layerId: number; asOf?: string; }
export interface FeatureHighlightPayload { entityId: string; }

// 将来追加予定: feature_edit_geometry, view_set_rotation など (コメントで残す)
```

```ts
// bridge/requestIdRegistry.ts
const seen = new Map<string, number>();
const TTL_MS = 5 * 60 * 1000;
export function markSeen(id: string): boolean {
  purgeExpired();
  if (seen.has(id)) return false;
  seen.set(id, Date.now());
  return true;
}
export function purgeExpired(): void {
  const now = Date.now();
  for (const [k, t] of seen) if (now - t > TTL_MS) seen.delete(k);
}
```

```ts
// bridge/webviewBridge.ts
type Handler = (m: Envelope) => void;
const handlers = new Set<Handler>();

declare global {
  interface Window { chrome?: { webview?: {
    postMessage(msg: unknown): void;
    addEventListener(t: 'message', l: (e: { data: string }) => void): void;
  } } }
}

const hostBridge = window.chrome?.webview;
if (!hostBridge && import.meta.env.DEV) console.warn('WebView2 host not detected (dev mode)');

hostBridge?.addEventListener('message', (e) => {
  let msg: Envelope;
  try { msg = JSON.parse(e.data) as Envelope; } catch { return; }
  if (msg.requestId && !markSeen(msg.requestId)) return;
  handlers.forEach(h => h(msg));
});

export function sendToHost<P>(msg: Envelope<P>): void {
  hostBridge?.postMessage(JSON.stringify(msg));
}
export function onMessage(h: Handler): () => void {
  handlers.add(h);
  return () => handlers.delete(h);
}
```

注意点:
- WebView2 の addEventListener は `e.data` が文字列。JSON.parse 必須
- envelope を文字列化して `postMessage` する規約に統一（双方向）

## テスト観点
- 0404 Vitest で `markSeen` と JSON シリアライズの単体
