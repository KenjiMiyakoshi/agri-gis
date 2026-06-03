-- E101 (WE1): layer_history テーブル新設
-- Phase A feature_history (003_feature_history.sql) と同形のバイテンポラル退避先。
-- fn_layer_update / fn_layer_delete v2 が旧行をここに退避し、半開区間 [valid_from, valid_to) を閉じる。
-- valid_from/_to は DATE 型 (PHASE_E_DESIGN_P §2.1 ユーザー判断 1 = feature と統一)。

CREATE TABLE IF NOT EXISTS layer_history (
    history_id       BIGSERIAL    PRIMARY KEY,
    layer_id         INTEGER      NOT NULL,                          -- layers.layer_id への論理 FK (CASCADE しない、history は永続)
    layer_name       TEXT         NOT NULL,
    layer_type       TEXT         NOT NULL,
    geometry_type    TEXT         NULL,
    description      TEXT         NULL,
    source_format    TEXT         NULL,
    source_srid      INTEGER      NULL,
    schema_version   INTEGER      NOT NULL,
    schema_json      JSONB        NOT NULL,
    style_json       JSONB        NOT NULL DEFAULT '{}'::jsonb,
    owner_org_id     INTEGER      NULL,
    is_shared        BOOLEAN      NOT NULL,
    created_by       UUID         NULL,
    created_org_id   INTEGER      NULL,
    version          INTEGER      NOT NULL,                          -- 退避時の version 値
    valid_from       DATE         NOT NULL,
    valid_to         DATE         NOT NULL,
    created_at       TIMESTAMPTZ  NOT NULL,
    updated_at       TIMESTAMPTZ  NOT NULL,
    archived_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    archived_by      TEXT         NOT NULL,
    archived_reason  TEXT         NOT NULL CHECK (archived_reason IN ('update', 'delete'))
);

CREATE INDEX IF NOT EXISTS ix_layer_history_layer_id_valid
    ON layer_history (layer_id, valid_from, valid_to);

CREATE INDEX IF NOT EXISTS ix_layer_history_archived_at
    ON layer_history (archived_at DESC);

COMMENT ON TABLE layer_history IS
    'Phase E E101: layers の旧バージョン退避先 (Phase A feature_history 同型)。fn_layer_update/delete v2 で書き込む。';
COMMENT ON COLUMN layer_history.valid_from IS
    '退避時点での active 期間の開始日。fn_layer_create か前回の fn_layer_update が立てた CURRENT_DATE。';
COMMENT ON COLUMN layer_history.valid_to IS
    '退避時点でのこの版の有効終了日。fn_layer_update/delete v2 が CURRENT_DATE で閉じる。';
COMMENT ON COLUMN layer_history.archived_reason IS
    'update=fn_layer_update で退避、delete=fn_layer_delete v2 で退避';
