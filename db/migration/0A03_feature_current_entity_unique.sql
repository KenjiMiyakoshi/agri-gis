-- A103: feature_current(entity_id) UNIQUE INDEX 追加 (Review② H1 修復)
-- PL/pgSQL 関数 (fn_feature_update / fn_feature_delete 等) が暗黙に依存している
-- 「entity_id 一意」を DB レベルで物理的に強制する。

CREATE UNIQUE INDEX IF NOT EXISTS ux_feature_current_entity_id
    ON feature_current(entity_id);
