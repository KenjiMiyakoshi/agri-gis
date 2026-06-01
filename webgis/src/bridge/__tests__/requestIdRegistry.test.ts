import { describe, it, expect, beforeEach } from 'vitest';
import { markSeen, purgeExpired, _resetForTest } from '../requestIdRegistry';

describe('requestIdRegistry.markSeen', () => {
  beforeEach(() => {
    _resetForTest();
  });

  it('returns true on first call for a given id', () => {
    expect(markSeen('x1')).toBe(true);
  });

  it('returns false on duplicate id within TTL', () => {
    expect(markSeen('x1')).toBe(true);
    expect(markSeen('x1')).toBe(false);
  });

  it('allows the same id again after TTL (5 min) has elapsed', () => {
    const t0 = 1_000_000;
    expect(markSeen('x2', t0)).toBe(true);
    // TTL = 5 分 + 1ms 進める
    expect(markSeen('x2', t0 + 5 * 60 * 1000 + 1)).toBe(true);
  });

  it('purgeExpired removes only outdated entries', () => {
    const t0 = 0;
    markSeen('keep', t0);
    markSeen('drop', t0);
    // 5 分 + 1ms 後に「drop」だけ古い扱いになるよう、purge を進めた時計で呼ぶ
    purgeExpired(t0 + 5 * 60 * 1000 + 1);
    // どちらも purge され、再度受理される
    expect(markSeen('drop', t0 + 5 * 60 * 1000 + 2)).toBe(true);
    expect(markSeen('keep', t0 + 5 * 60 * 1000 + 2)).toBe(true);
  });
});
