import type { Envelope } from './messages';
import { markSeen } from './requestIdRegistry';

// WebView2 から提供される window.chrome.webview API の最低限の型
interface WebView2Host {
  postMessage(msg: unknown): void;
  addEventListener(type: 'message', listener: (e: { data: string }) => void): void;
  removeEventListener(type: 'message', listener: (e: { data: string }) => void): void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: WebView2Host;
    };
  }
}

type Handler = (m: Envelope) => void;
const handlers = new Set<Handler>();

const host: WebView2Host | undefined = window.chrome?.webview;

if (!host && import.meta.env.DEV) {
  console.warn('[bridge] WebView2 host not detected (dev mode). sendToHost is no-op.');
}

host?.addEventListener('message', (e) => {
  let msg: Envelope;
  try {
    msg = JSON.parse(e.data) as Envelope;
  } catch {
    console.warn('[bridge] received non-JSON message');
    return;
  }
  if (msg.requestId && !markSeen(msg.requestId)) {
    return;
  }
  for (const h of handlers) {
    h(msg);
  }
});

export function sendToHost<P>(msg: Envelope<P>): void {
  if (!host) {
    return;
  }
  host.postMessage(JSON.stringify(msg));
}

// 受信ハンドラ登録。返り値の関数を呼ぶと登録解除される。
export function onMessage(h: Handler): () => void {
  handlers.add(h);
  return () => {
    handlers.delete(h);
  };
}

// 開発環境でのみ true（hosted の判定が必要なケースで使う）
export function isHosted(): boolean {
  return host !== undefined;
}
