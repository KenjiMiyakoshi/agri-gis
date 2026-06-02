-- D102 (WD1): selection_sets テーブル新設
-- Phase D 採用案 §2.3: WebGIS で選択された entity_ids を sid で保存
-- raster overlay tile (GET /tiles/selection/{sid}/{z}/{x}/{y}.png) の入力源
-- user_id は ICurrentUser.UserId、color_hex は WebGIS 側 highlight 色 (デフォルト amber)
-- session_id FK は 0D04 で追加 (二段階 migration、CASCADE 経路は最終的に session_id 経由)
-- entity_ids は UUID[] (gist index は CQL_FILTER 経由のため不要)

CREATE TABLE IF NOT EXISTS selection_sets (
    sid         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID         NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    entity_ids  UUID[]       NOT NULL,
    color_hex   TEXT         NOT NULL DEFAULT '#FFEB3B',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_selection_sets_user_id
    ON selection_sets(user_id);

-- color_hex のサニタイズ
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.check_constraints
         WHERE constraint_name = 'selection_sets_color_hex_check'
    ) THEN
        ALTER TABLE selection_sets ADD CONSTRAINT selection_sets_color_hex_check
            CHECK (color_hex ~ '^#[0-9A-Fa-f]{6}$');
    END IF;
END $$;

-- entity_ids の上限 (50000 件、Phase D ISSUES INDEX D202 受け入れ条件)
-- DDL では check できないので、API 側 (D202) で validation する
-- (PostgreSQL の array_length は INDEX に使えないため、本 migration では制約なし)

COMMENT ON TABLE selection_sets IS
    'Phase D D102: WebGIS 選択集合を sid で永続化。raster overlay の入力源。';
COMMENT ON COLUMN selection_sets.entity_ids IS
    'feature_current.entity_id の配列。CQL_FILTER で展開。上限 50000 件は API 側 validation。';
COMMENT ON COLUMN selection_sets.color_hex IS
    'WebGIS highlight 色 (#RRGGBB)。デフォルト #FFEB3B (amber)。';
