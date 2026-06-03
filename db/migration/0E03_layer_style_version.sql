-- E103 (WE1): layer_style_version テーブル新設
-- Phase A layer_schema_version (005_layer_schema_version.sql) の完全コピー設計。
-- PUT /api/admin/layers/{id}/style → fn_layer_style_upsert (E106) で履歴 append + valid_to 接合。

CREATE TABLE IF NOT EXISTS layer_style_version (
    layer_id       INTEGER      NOT NULL REFERENCES layers(layer_id) ON DELETE RESTRICT,
    style_version  INTEGER      NOT NULL,
    style_json     JSONB        NOT NULL,
    valid_from     DATE         NOT NULL DEFAULT CURRENT_DATE,
    valid_to       DATE         NOT NULL DEFAULT '9999-12-31'::date,
    created_by     UUID         NULL REFERENCES users(user_id) ON DELETE SET NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (layer_id, style_version)
);

CREATE INDEX IF NOT EXISTS ix_layer_style_version_active
    ON layer_style_version (layer_id, valid_from, valid_to);

CREATE INDEX IF NOT EXISTS ix_layer_style_version_layer_active
    ON layer_style_version (layer_id) WHERE valid_to = '9999-12-31'::date;

-- 半開区間の整合性
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.check_constraints
         WHERE constraint_name = 'layer_style_version_period_check'
    ) THEN
        ALTER TABLE layer_style_version ADD CONSTRAINT layer_style_version_period_check
            CHECK (valid_from <= valid_to);
    END IF;
END $$;

-- 既存 layers.style_json の active 行を style_version=1 で初期 INSERT
-- (Phase D で追加された style_json を Phase E 履歴管理に取り込む初回マイグレーション)
INSERT INTO layer_style_version (layer_id, style_version, style_json, valid_from, valid_to, created_by, created_at)
SELECT l.layer_id, 1, l.style_json, CURRENT_DATE, '9999-12-31'::date, NULL, now()
  FROM layers l
 WHERE l.valid_to = '9999-12-31'::date
   AND NOT EXISTS (
       SELECT 1 FROM layer_style_version lsv
        WHERE lsv.layer_id = l.layer_id
   );

COMMENT ON TABLE layer_style_version IS
    'Phase E E103: layers.style_json (theme/SLD 定義) の履歴管理。layer_schema_version 同型。fn_layer_style_upsert (E106) で append。';
