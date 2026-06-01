# 0404: Vitest 最小セットアップ + envelope / 重複検知テスト

| 項目 | 値 |
|---|---|
| Phase | WebGIS |
| Estimate | 0.5d |
| Depends on | 0403 |
| Blocks | なし |

## 概要
Vitest を WebGIS プロジェクトに導入し、envelope の JSON シリアライズと `requestIdRegistry` の重複検知を最小限テストする。

## 背景・目的
案 B' は WebGIS のテストを **最小** に絞る方針。OL の見た目や DOM 結合はテストしないが、bridge 周りの不変条件だけは固める。

## スコープ
### 含む
- `vitest` を `devDependencies` に追加
- `webgis/vitest.config.ts`（jsdom 環境または node。今回は node で十分）
- `webgis/src/bridge/__tests__/requestIdRegistry.test.ts`
  - 同じ id は 2 回目 false
  - 5 分超過の擬似テスト (`vi.useFakeTimers` で時計を進める)
- `webgis/src/bridge/__tests__/messages.test.ts`
  - envelope の JSON.stringify → parse round-trip
  - requestId 欠落 OK、文字列の場合のみ重複検知される
- `package.json` に `"test": "vitest run"` を追加

### 含まない
- jsdom 経由の DOM テスト
- 結合テスト

## 受け入れ条件 (Acceptance Criteria)
- [ ] `npm run test` が pass
- [ ] テストファイルが 2 つ、合計 4 ケース以上
- [ ] CI でも動く構成（Node のみで完結）

## 影響ファイル
- `D:\proj\agri-gis\webgis\package.json` (devDependencies, scripts)
- `D:\proj\agri-gis\webgis\vitest.config.ts` (新規)
- `D:\proj\agri-gis\webgis\src\bridge\__tests__\requestIdRegistry.test.ts` (新規)
- `D:\proj\agri-gis\webgis\src\bridge\__tests__\messages.test.ts` (新規)

## 実装ノート
```ts
// requestIdRegistry.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { markSeen } from '../requestIdRegistry';

describe('markSeen', () => {
  beforeEach(() => vi.useFakeTimers());
  it('returns true on first call, false on second', () => {
    expect(markSeen('x1')).toBe(true);
    expect(markSeen('x1')).toBe(false);
  });
  it('expires after 5 min', () => {
    markSeen('x2');
    vi.advanceTimersByTime(5 * 60 * 1000 + 1);
    expect(markSeen('x2')).toBe(true);
  });
});
```

注意点:
- `requestIdRegistry.ts` がモジュールスコープに `Map` を持つので、テスト間で状態が漏れる。`beforeEach` で resetModules するか、テストごとに違う id を使う

## テスト観点
- envelope の round-trip
- 重複検知の境界
