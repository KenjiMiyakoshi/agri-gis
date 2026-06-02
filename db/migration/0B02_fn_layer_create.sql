-- B102 (WB1): fn_layer_create
-- layers INSERT → layer_schema_version INSERT → audit_log INSERT を 1 関数でアトミック化。
-- 戻り値 = layer_id。
-- p_schema_json NULL は '[]' (空 fields) 扱い。
-- audit_log.after_doc は to_jsonb(new_row) (layers に geom 列なし、C2 影響なし)。

CREATE OR REPLACE FUNCTION fn_layer_create(
    p_name           TEXT,
    p_layer_type     TEXT,
    p_geometry_type  TEXT,
    p_source_format  TEXT,
    p_source_srid    INT,
    p_description    TEXT,
    p_schema_json    JSONB,
    p_actor          TEXT,
    p_request_id     TEXT,
    p_user_id        UUID,
    p_org_id         INT
) RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    v_layer_id      INT;
    v_schema_json   JSONB;
    v_after_doc     JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;
    IF p_name IS NULL OR length(trim(p_name)) = 0 THEN
        RAISE EXCEPTION 'name is required' USING ERRCODE = '22023';
    END IF;
    IF p_layer_type IS NULL OR length(trim(p_layer_type)) = 0 THEN
        RAISE EXCEPTION 'layer_type is required' USING ERRCODE = '22023';
    END IF;
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required' USING ERRCODE = '22023';
    END IF;
    IF p_org_id IS NULL THEN
        RAISE EXCEPTION 'org_id is required' USING ERRCODE = '22023';
    END IF;

    v_schema_json := COALESCE(p_schema_json, '{"fields":[]}'::jsonb);

    INSERT INTO layers (
        layer_name, layer_type, geometry_type,
        source_format, source_srid, description,
        schema_json, schema_version,
        created_by, created_org_id,
        created_at, updated_at
    ) VALUES (
        p_name, p_layer_type, p_geometry_type,
        p_source_format, p_source_srid, p_description,
        v_schema_json, 1,
        p_user_id, p_org_id,
        now(), now()
    )
    RETURNING layer_id INTO v_layer_id;

    INSERT INTO layer_schema_version (
        layer_id, schema_version, schema_json,
        valid_from, valid_to, created_by
    ) VALUES (
        v_layer_id, 1, v_schema_json,
        now(), NULL, p_actor
    );

    SELECT to_jsonb(l.*) INTO v_after_doc
      FROM layers l
     WHERE l.layer_id = v_layer_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'layer_create', 'layers',
        v_layer_id, NULL, NULL,
        NULL, v_after_doc, p_request_id
    );

    RETURN v_layer_id;
END;
$$;

COMMENT ON FUNCTION fn_layer_create IS 'Phase B B102: layers + layer_schema_version + audit_log を 1 Tx で書く新規レイヤ作成';
