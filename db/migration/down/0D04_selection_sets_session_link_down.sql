-- D102 (WD1) ロールバック
-- 0D04 の逆操作: session_id FK + 列を削除

DROP INDEX IF EXISTS ix_selection_sets_session_id;
ALTER TABLE selection_sets DROP CONSTRAINT IF EXISTS fk_selection_sets_session;
ALTER TABLE selection_sets DROP COLUMN IF EXISTS session_id;
