-- WE0 PoC: 50 万件 feature_current + 100 万件 feature_history を layer_id=1000 で生成
-- 場所: 帯広付近のグリッド (z=15 タイル数枚に収まる範囲)
-- 履歴: 2024-01-01 / 2024-06-01 / 2024-12-31 の 3 valid_from で 3 世代

BEGIN;

-- まず PoC 用 layer を作成 (存在しなければ)
INSERT INTO layers (layer_id, layer_name, layer_type, owner_org_id, is_shared, schema_version, schema_json)
VALUES (1000, 'PoC layer for WE0', 'polygon', 1, true, 1, '{"fields":[]}'::jsonb)
ON CONFLICT (layer_id) DO NOTHING;

-- 既存 PoC データを clear
DELETE FROM feature_history WHERE layer_id = 1000;
DELETE FROM feature_current WHERE layer_id = 1000;

-- feature_current: 50 万件 (現在版、valid_from='2025-01-01', valid_to='9999-12-31')
-- 中心 (143.205, 42.9115) を 3857 で 約 (15940484, 5298510) として、東西 4km × 南北 4km 内に散らす
-- 50 万件のグリッド: sqrt(500000) ≒ 707、706x708 グリッドで 4km × 4km → 約 5.6m × 5.7m ポリゴン
INSERT INTO feature_current (layer_id, entity_id, attributes_schema_version, version, valid_from, valid_to,
                              created_by, updated_by, created_at, updated_at, attributes, geom)
SELECT
  1000,
  gen_random_uuid(),
  1,
  1,
  '2025-01-01'::date,
  '9999-12-31'::date,
  'poc-current',
  'poc-current',
  '2025-01-01 00:00:00+00'::timestamptz,
  '2025-01-01 00:00:00+00'::timestamptz,
  jsonb_build_object('idx', g),
  ST_MakeEnvelope(
    15938000 + (g % 707) * 5.7,
    5296500 + (g / 707) * 5.7,
    15938000 + (g % 707) * 5.7 + 5,
    5296500 + (g / 707) * 5.7 + 5,
    3857
  )::geometry(Geometry, 3857)
FROM generate_series(1, 500000) AS g;

-- feature_history: 各 50 万件で 2 世代 (2024-01-01〜2024-06-30 と 2024-07-01〜2024-12-31)
-- = 100 万件
INSERT INTO feature_history (feature_id, layer_id, entity_id, attributes_schema_version, version, valid_from, valid_to,
                              created_by, updated_by, created_at, updated_at, attributes, geom,
                              archived_by, archived_reason)
SELECT
  nextval('feature_current_feature_id_seq'),
  1000,
  gen_random_uuid(),
  1,
  1,
  '2024-01-01'::date,
  '2024-07-01'::date,
  'poc-history-v1',
  'poc-history-v1',
  '2024-01-01 00:00:00+00'::timestamptz,
  '2024-07-01 00:00:00+00'::timestamptz,
  jsonb_build_object('idx', g, 'gen', 1),
  ST_MakeEnvelope(
    15938000 + (g % 707) * 5.7,
    5296500 + (g / 707) * 5.7,
    15938000 + (g % 707) * 5.7 + 5,
    5296500 + (g / 707) * 5.7 + 5,
    3857
  )::geometry(Geometry, 3857),
  'poc-archiver',
  'update'
FROM generate_series(1, 500000) AS g;

INSERT INTO feature_history (feature_id, layer_id, entity_id, attributes_schema_version, version, valid_from, valid_to,
                              created_by, updated_by, created_at, updated_at, attributes, geom,
                              archived_by, archived_reason)
SELECT
  nextval('feature_current_feature_id_seq'),
  1000,
  gen_random_uuid(),
  1,
  2,
  '2024-07-01'::date,
  '2025-01-01'::date,
  'poc-history-v2',
  'poc-history-v2',
  '2024-07-01 00:00:00+00'::timestamptz,
  '2025-01-01 00:00:00+00'::timestamptz,
  jsonb_build_object('idx', g, 'gen', 2),
  ST_MakeEnvelope(
    15938000 + (g % 707) * 5.7,
    5296500 + (g / 707) * 5.7,
    15938000 + (g % 707) * 5.7 + 5,
    5296500 + (g / 707) * 5.7 + 5,
    3857
  )::geometry(Geometry, 3857),
  'poc-archiver',
  'update'
FROM generate_series(1, 500000) AS g;

-- feature_asof view を CREATE OR REPLACE (PoC、本実装は WE1 0E07)
CREATE OR REPLACE VIEW feature_asof AS
SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
       attributes_schema_version, created_by, updated_by, created_at, updated_at,
       attributes, geom
  FROM feature_current
UNION ALL
SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
       attributes_schema_version, created_by, updated_by, created_at, updated_at,
       attributes, geom
  FROM feature_history;

-- ANALYZE for query planner
ANALYZE feature_current;
ANALYZE feature_history;

COMMIT;

-- 確認
SELECT
  (SELECT COUNT(*) FROM feature_current WHERE layer_id=1000) AS current_count,
  (SELECT COUNT(*) FROM feature_history WHERE layer_id=1000) AS history_count,
  (SELECT COUNT(*) FROM feature_asof WHERE layer_id=1000) AS asof_total;
