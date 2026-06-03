-- E103 (WE1) ロールバック: layer_style_version 全削除
-- 0E06 が layer_style_version に書き込むので先に関数を down

DROP INDEX IF EXISTS ix_layer_style_version_layer_active;
DROP INDEX IF EXISTS ix_layer_style_version_active;
DROP TABLE IF EXISTS layer_style_version CASCADE;
