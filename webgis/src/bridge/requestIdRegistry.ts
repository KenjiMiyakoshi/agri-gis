// requestId の重複検知。Map<id, timestamp>、5分の TTL。
// 同一 requestId のメッセージが2回到着したら2回目以降は false を返す。

const seen = new Map<string, number>();
const TTL_MS = 5 * 60 * 1000;

export function markSeen(id: string, now: number = Date.now()): boolean {
  purgeExpired(now);
  if (seen.has(id)) {
    return false;
  }
  seen.set(id, now);
  return true;
}

export function purgeExpired(now: number = Date.now()): void {
  for (const [k, t] of seen) {
    if (now - t > TTL_MS) {
      seen.delete(k);
    }
  }
}

// テスト用：Vitest で内部状態をリセットするための公開
export function _resetForTest(): void {
  seen.clear();
}
