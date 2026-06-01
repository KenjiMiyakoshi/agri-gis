-- 0106: layer_schema_version テーブル新設 + 初日稼働シード
-- append-only でスキーマ履歴を保持。valid_to IS NULL が現行スキーマ。
-- 既存レイヤぶんの初期行を投入（schema_version=1, schema_json は layers から取得）。

CREATE TABLE IF NOT EXISTS layer_schema_version (
    layer_id       INTEGER     NOT NULL REFERENCES layers(layer_id),
    schema_version INT         NOT NULL,
    schema_json    JSONB       NOT NULL,
    valid_from     TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to       TIMESTAMPTZ,
    created_by     TEXT        NOT NULL,
    PRIMARY KEY (layer_id, schema_version)
);

CREATE INDEX IF NOT EXISTS idx_layer_schema_version_layer
    ON layer_schema_version (layer_id, valid_from DESC);

-- 既存レイヤの初期行を投入（冪等：ON CONFLICT DO NOTHING）
INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
SELECT layer_id, schema_version, schema_json, now(), NULL, 'system'
  FROM layers
ON CONFLICT (layer_id, schema_version) DO NOTHING;
