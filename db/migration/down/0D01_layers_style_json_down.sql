-- D102 (WD1) ロールバック
-- 0D01 の逆操作: layers.style_json 列削除

ALTER TABLE layers DROP COLUMN IF EXISTS style_json;
