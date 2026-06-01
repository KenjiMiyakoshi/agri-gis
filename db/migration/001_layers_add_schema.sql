-- 0102: layers テーブルに schema_json / schema_version を追加
-- 冪等: ADD COLUMN IF NOT EXISTS

ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS schema_json JSONB NOT NULL DEFAULT '{"fields":[]}'::jsonb;

ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS schema_version INT NOT NULL DEFAULT 1;
