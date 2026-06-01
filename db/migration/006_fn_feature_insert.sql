-- 0107: fn_feature_insert
-- フィーチャを新規挿入し、audit_log に1行残す。
-- API 層 (0204) が actor / request_id をヘッダから取り出して引数で渡す。
-- 属性スキーマの構造バリデーションは API 層で行う。本関数は受け取った JSONB をそのまま保存。

CREATE OR REPLACE FUNCTION fn_feature_insert(
    p_layer_id          INT,
    p_entity_id         UUID,
    p_geom_geojson_4326 TEXT,
    p_attributes        JSONB,
    p_actor             TEXT,
    p_request_id        TEXT
) RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_feature_id     BIGINT;
    v_schema_version INT;
    v_geom           geometry;
    v_after_doc      JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT schema_version INTO v_schema_version
      FROM layers
     WHERE layer_id = p_layer_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found: %', p_layer_id USING ERRCODE = '23503';
    END IF;

    v_geom := ST_Transform(
                ST_SetSRID(ST_GeomFromGeoJSON(p_geom_geojson_4326), 4326),
                3857
              );

    INSERT INTO feature_current (
        layer_id, entity_id, geom, attributes,
        valid_from, valid_to,
        created_at, updated_at, created_by, updated_by,
        version, attributes_schema_version
    ) VALUES (
        p_layer_id, p_entity_id, v_geom, COALESCE(p_attributes, '{}'::jsonb),
        CURRENT_DATE, DATE '9999-12-31',
        now(), now(), p_actor, p_actor,
        1, v_schema_version
    )
    RETURNING feature_id INTO v_feature_id;

    SELECT to_jsonb(fc.*) INTO v_after_doc
      FROM feature_current fc
     WHERE feature_id = v_feature_id;

    INSERT INTO audit_log (
        actor, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, 'feature_insert', 'feature_current',
        p_layer_id, p_entity_id, v_feature_id,
        NULL, v_after_doc, p_request_id
    );

    RETURN v_feature_id;
END;
$$;
