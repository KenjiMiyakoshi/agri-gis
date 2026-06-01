-- 0104: feature_history テーブル新設
-- 旧バージョンの feature を退避する場所。UPDATE/DELETE 時に PL/pgSQL 関数 (0108/0109) で書き込む。

CREATE TABLE IF NOT EXISTS feature_history (
    history_id                BIGSERIAL PRIMARY KEY,
    feature_id                BIGINT      NOT NULL,
    layer_id                  INTEGER     NOT NULL REFERENCES layers(layer_id),
    entity_id                 UUID        NOT NULL,
    geom                      geometry(Geometry, 3857),
    attributes                JSONB       NOT NULL DEFAULT '{}'::jsonb,
    attributes_schema_version INT         NOT NULL,
    valid_from                DATE        NOT NULL,
    valid_to                  DATE        NOT NULL,
    version                   INT         NOT NULL,
    created_at                TIMESTAMPTZ NOT NULL,
    updated_at                TIMESTAMPTZ NOT NULL,
    created_by                TEXT        NOT NULL,
    updated_by                TEXT        NOT NULL,
    archived_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    archived_by               TEXT        NOT NULL,
    archived_reason           TEXT        NOT NULL CHECK (archived_reason IN ('update', 'delete'))
);

CREATE INDEX IF NOT EXISTS idx_feature_history_entity
    ON feature_history (entity_id, valid_to DESC);

CREATE INDEX IF NOT EXISTS idx_feature_history_layer
    ON feature_history (layer_id);

CREATE INDEX IF NOT EXISTS idx_feature_history_geom
    ON feature_history USING GIST (geom);
