-- D102 (WD1) ロールバック
-- 0D03 の逆操作: selection_sets 全削除
-- 0D04 を先にロールバックしてから本ファイル

DROP TABLE IF EXISTS selection_sets CASCADE;
