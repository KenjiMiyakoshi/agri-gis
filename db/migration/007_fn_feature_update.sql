-- 0108: fn_feature_update (楽観ロック付き)
-- 旧行を feature_history に退避してから current を更新、audit_log に1行残す。
-- expected_version 不一致は ERRCODE='40001' (API 層で 409)。
-- p_new_geom / p_new_attributes が NULL の場合はその列を据え置く。

CREATE OR REPLACE FUNCTION fn_feature_update(
    p_entity_id             UUID,
    p_new_geom_geojson_4326 TEXT,
    p_new_attributes        JSONB,
    p_actor                 TEXT,
    p_expected_version      INT,
    p_request_id            TEXT,
    OUT new_version         INT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_cur      feature_current%ROWTYPE;
    v_before   JSONB;
    v_after    JSONB;
    v_new_geom geometry;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_cur
      FROM feature_current
     WHERE entity_id = p_entity_id
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'entity not found: %', p_entity_id USING ERRCODE = '02000';
    END IF;

    IF v_cur.version <> p_expected_version THEN
        RAISE EXCEPTION 'version mismatch: expected %, current %',
            p_expected_version, v_cur.version
            USING ERRCODE = '40001';
    END IF;

    SELECT to_jsonb(v_cur) INTO v_before;

    -- 旧行を history へ退避
    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from, v_cur.valid_to,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'update'
    );

    -- 新しい geom があれば変換、なければ NULL を保持
    IF p_new_geom_geojson_4326 IS NOT NULL THEN
        v_new_geom := ST_Transform(
                        ST_SetSRID(ST_GeomFromGeoJSON(p_new_geom_geojson_4326), 4326),
                        3857
                      );
    END IF;

    UPDATE feature_current SET
        geom        = COALESCE(v_new_geom, geom),
        attributes  = COALESCE(p_new_attributes, attributes),
        updated_at  = now(),
        updated_by  = p_actor,
        version     = v_cur.version + 1
     WHERE entity_id = p_entity_id;

    new_version := v_cur.version + 1;

    SELECT to_jsonb(fc.*) INTO v_after
      FROM feature_current fc
     WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (
        actor, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, 'feature_update', 'feature_current',
        v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
        v_before, v_after, p_request_id
    );
END;
$$;
