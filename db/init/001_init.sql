CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE layers (
    layer_id SERIAL PRIMARY KEY,
    layer_name TEXT NOT NULL,
    layer_type TEXT NOT NULL,
    owner_org_id INTEGER,
    is_shared BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP DEFAULT now()
);

CREATE TABLE feature_current (
    feature_id BIGSERIAL PRIMARY KEY,
    layer_id INTEGER NOT NULL REFERENCES layers(layer_id),
    entity_id UUID NOT NULL,
    geom geometry(Geometry, 3857),
    attributes JSONB DEFAULT '{}'::jsonb,
    valid_from DATE NOT NULL DEFAULT CURRENT_DATE,
    valid_to DATE NOT NULL DEFAULT DATE '9999-12-31',
    created_at TIMESTAMP DEFAULT now(),
    updated_at TIMESTAMP DEFAULT now()
);

CREATE INDEX idx_feature_current_geom
ON feature_current
USING GIST (geom);

CREATE INDEX idx_feature_current_layer
ON feature_current(layer_id);
