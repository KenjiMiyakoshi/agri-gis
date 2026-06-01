-- 0105: audit_log テーブル新設
-- 全書き込み (feature_insert/update/delete, schema_upsert) を独立して記録する。
-- PL/pgSQL 関数 (0107-0110) が各書き込みのトランザクション内で INSERT する。

CREATE TABLE IF NOT EXISTS audit_log (
    audit_id     BIGSERIAL   PRIMARY KEY,
    occurred_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    actor        TEXT        NOT NULL,
    action       TEXT        NOT NULL,   -- 'feature_insert' | 'feature_update' | 'feature_delete' | 'schema_upsert' 等
    target_table TEXT        NOT NULL,   -- 'feature_current' | 'layers' 等
    layer_id     INTEGER,
    entity_id    UUID,
    feature_id   BIGINT,
    before_doc   JSONB,
    after_doc    JSONB,
    request_id   TEXT
);

CREATE INDEX IF NOT EXISTS idx_audit_log_occurred
    ON audit_log (occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_audit_log_target
    ON audit_log (target_table, entity_id);
