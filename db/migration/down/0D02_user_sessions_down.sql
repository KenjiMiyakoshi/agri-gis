-- D102 (WD1) ロールバック
-- 0D02 の逆操作: user_sessions 全削除
-- 0D03 / 0D04 を先にロールバックしてから本ファイル (CASCADE は付けるが安全策)

DROP TABLE IF EXISTS user_sessions CASCADE;
