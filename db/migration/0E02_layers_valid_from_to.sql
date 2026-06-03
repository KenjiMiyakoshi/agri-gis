-- E102 (WE1): layers に valid_from/_to/version 列を追加 + 既存 deleted_at IS NOT NULL の backfill
-- PHASE_E_DESIGN_P §2.1 ユーザー判断 1 = DATE 型で統一。
-- deleted_at 列は Phase E では二重書きで残置、Phase E' で参照削除 + DROP COLUMN (ユーザー判断 3)。

-- 列追加。NOT NULL は backfill 後に SET NOT NULL する (既存行が NULL のため)
ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS valid_from DATE    NULL,
    ADD COLUMN IF NOT EXISTS valid_to   DATE    NULL,
    ADD COLUMN IF NOT EXISTS version    INTEGER NULL;

-- backfill:
--   既存 active:    valid_from=created_at::date, valid_to='9999-12-31'
--   既存 deleted: valid_from=created_at::date, valid_to=deleted_at::date
--                 (削除日が valid_to、半開区間 [created_at, deleted_at) を満たす)
UPDATE layers
   SET valid_from = COALESCE(valid_from, created_at::date),
       valid_to   = COALESCE(
                       valid_to,
                       CASE WHEN deleted_at IS NOT NULL
                            THEN deleted_at::date
                            ELSE '9999-12-31'::date
                       END
                   ),
       version    = COALESCE(version, 1);

-- backfill 完了後、NOT NULL + DEFAULT を確定
ALTER TABLE layers
    ALTER COLUMN valid_from SET NOT NULL,
    ALTER COLUMN valid_from SET DEFAULT CURRENT_DATE,
    ALTER COLUMN valid_to   SET NOT NULL,
    ALTER COLUMN valid_to   SET DEFAULT '9999-12-31'::date,
    ALTER COLUMN version    SET NOT NULL,
    ALTER COLUMN version    SET DEFAULT 1;

-- 半開区間の整合性 CHECK
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.check_constraints
         WHERE constraint_name = 'layers_valid_period_check'
    ) THEN
        ALTER TABLE layers ADD CONSTRAINT layers_valid_period_check
            CHECK (valid_from <= valid_to);
    END IF;
END $$;

-- active layer の高速検索 (UPDATE/DELETE で使う、Phase E API でも使う)
CREATE INDEX IF NOT EXISTS ix_layers_active_period
    ON layers (layer_id) WHERE valid_to = '9999-12-31'::date;

COMMENT ON COLUMN layers.valid_from IS
    'Phase E E102: この版が有効になった日 (fn_layer_create or fn_layer_update が CURRENT_DATE で立てる)';
COMMENT ON COLUMN layers.valid_to IS
    'Phase E E102: この版の有効終了日 (デフォルト 9999-12-31 = active)。fn_layer_update/delete v2 が CURRENT_DATE で閉じる';
COMMENT ON COLUMN layers.version IS
    'Phase E E102: 楽観ロック用 version。fn_layer_update で +1。クライアントは If-Match: {version} で送る';
