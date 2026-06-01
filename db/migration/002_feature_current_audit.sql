-- 0103: feature_current に監査・楽観ロック・スキーマ追跡列を追加、created_at/updated_at を TIMESTAMPTZ 化
-- 冪等: ADD COLUMN IF NOT EXISTS。TYPE 変換は既に TIMESTAMPTZ なら no-op になる前提でガード（pg_typeof チェック）。

BEGIN;

ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS created_by TEXT NOT NULL DEFAULT 'system';
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS updated_by TEXT NOT NULL DEFAULT 'system';
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS version INT NOT NULL DEFAULT 1;
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS attributes_schema_version INT NOT NULL DEFAULT 1;

-- TIMESTAMP → TIMESTAMPTZ（既に timestamptz なら ALTER は no-op）
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'feature_current'
          AND column_name = 'created_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        EXECUTE 'ALTER TABLE feature_current
                 ALTER COLUMN created_at TYPE TIMESTAMPTZ
                 USING created_at AT TIME ZONE ''Asia/Tokyo''';
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'feature_current'
          AND column_name = 'updated_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        EXECUTE 'ALTER TABLE feature_current
                 ALTER COLUMN updated_at TYPE TIMESTAMPTZ
                 USING updated_at AT TIME ZONE ''Asia/Tokyo''';
    END IF;
END $$;

-- DEFAULT を now() に保つ
ALTER TABLE feature_current ALTER COLUMN created_at SET DEFAULT now();
ALTER TABLE feature_current ALTER COLUMN updated_at SET DEFAULT now();

COMMIT;
