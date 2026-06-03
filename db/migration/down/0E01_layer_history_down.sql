-- E101 (WE1) ロールバック: layer_history 全削除
-- 0E04 / 0E05 が layer_history に INSERT するので先に関数を down してから本ファイル

DROP INDEX IF EXISTS ix_layer_history_archived_at;
DROP INDEX IF EXISTS ix_layer_history_layer_id_valid;
DROP TABLE IF EXISTS layer_history CASCADE;
