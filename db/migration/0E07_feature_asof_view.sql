-- E106 (WE1): feature_asof VIEW
-- feature_current + feature_history を UNION ALL で繋ぐ。asOf クエリ (CQL_FILTER で valid_from/_to 絞り込み) の入口。
-- WE0 PoC で 50 万件 × z=15 タイル 343ms (asOf 無し) / 407ms (履歴経由) を確認済、GO 判定 (#216)。

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

COMMENT ON VIEW feature_asof IS
    'Phase E E106: feature_current ∪ feature_history。GeoServer が feature_asof featureType として参照、API は CQL_FILTER で valid_from/_to 絞り込み。';
