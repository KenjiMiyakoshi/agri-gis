-- E'102 ロールバック (best effort)
ALTER TABLE layers ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;
UPDATE layers SET deleted_at = now()
 WHERE valid_to <> '9999-12-31'::date AND deleted_at IS NULL;
