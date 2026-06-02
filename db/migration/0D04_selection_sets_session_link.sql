-- D102 (WD1): selection_sets.session_id FK 追加 (CASCADE)
-- Phase D 採用案 §2.4: logout (user_sessions.deleted_at 埋め) で関連 selection_sets が CASCADE 削除される
-- session_id は NULLABLE (既存 selection_sets レコードがあれば NULL のまま、Phase D 以降で API が NOT NULL を保証)
-- ON DELETE CASCADE は user_sessions レコードが物理削除されたとき
-- (logical delete = deleted_at 埋めの場合は API 側 D204 で明示的に DELETE FROM selection_sets WHERE session_id = ...)

ALTER TABLE selection_sets
    ADD COLUMN IF NOT EXISTS session_id UUID NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
         WHERE constraint_name = 'fk_selection_sets_session'
    ) THEN
        ALTER TABLE selection_sets
            ADD CONSTRAINT fk_selection_sets_session
            FOREIGN KEY (session_id) REFERENCES user_sessions(session_id) ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_selection_sets_session_id
    ON selection_sets(session_id);

COMMENT ON COLUMN selection_sets.session_id IS
    'Phase D D102: user_sessions 親テーブルとの FK。NULL=Phase D 移行前のレガシー、Phase D 以降は API が必須化。';
