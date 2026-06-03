-- E102 (WE1) ロールバック: layers の valid_from/_to/version + index/CHECK 削除

DROP INDEX IF EXISTS ix_layers_active_period;
ALTER TABLE layers DROP CONSTRAINT IF EXISTS layers_valid_period_check;
ALTER TABLE layers DROP COLUMN IF EXISTS valid_from;
ALTER TABLE layers DROP COLUMN IF EXISTS valid_to;
ALTER TABLE layers DROP COLUMN IF EXISTS version;
