-- E105 (WE1): fn_layer_delete v2 (CREATE OR REPLACE で 0B03 を置換)
-- 旧: layers.deleted_at = now() のみ
-- 新: 旧行を layer_history に退避 + layers.valid_to = CURRENT_DATE + deleted_at = now() (二重書き)
-- PHASE_E_DESIGN_P §2.3 ユーザー判断 3: deleted_at は Phase E では二重書き継続、Phase E' で DROP

CREATE OR REPLACE FUNCTION fn_layer_delete(
    p_layer_id   INT,
    p_actor      TEXT,
    p_request_id TEXT,
    p_user_id    UUID,
    p_org_id     INT
) RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_before_doc  JSONB;
    v_after_doc   JSONB;
    v_current_row layers%ROWTYPE;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required' USING ERRCODE = '22023';
    END IF;
    IF p_org_id IS NULL THEN
        RAISE EXCEPTION 'org_id is required' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_current_row
      FROM layers
     WHERE layers.layer_id = p_layer_id
       AND valid_to = '9999-12-31'::date
       AND deleted_at IS NULL
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    v_before_doc := to_jsonb(v_current_row);

    -- 旧行を layer_history に退避 (valid_to = CURRENT_DATE で閉じる、archived_reason='delete')
    INSERT INTO layer_history (
        layer_id, layer_name, layer_type, geometry_type, description,
        source_format, source_srid, schema_version, schema_json, style_json,
        owner_org_id, is_shared, created_by, created_org_id, version,
        valid_from, valid_to, created_at, updated_at,
        archived_by, archived_reason
    ) VALUES (
        v_current_row.layer_id, v_current_row.layer_name, v_current_row.layer_type,
        v_current_row.geometry_type, v_current_row.description,
        v_current_row.source_format, v_current_row.source_srid,
        v_current_row.schema_version, v_current_row.schema_json, v_current_row.style_json,
        v_current_row.owner_org_id, v_current_row.is_shared,
        v_current_row.created_by, v_current_row.created_org_id, v_current_row.version,
        v_current_row.valid_from, CURRENT_DATE,
        v_current_row.created_at, v_current_row.updated_at,
        p_actor, 'delete'
    );

    -- layers 本体は deleted_at と valid_to を両方立てる (Phase E 二重書き)
    UPDATE layers
       SET deleted_at = now(),
           valid_to   = CURRENT_DATE,
           updated_at = now()
     WHERE layers.layer_id = p_layer_id;

    SELECT to_jsonb(l.*) INTO v_after_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'layer_delete', 'layers',
        p_layer_id, NULL, NULL,
        v_before_doc, v_after_doc, p_request_id
    );
END;
$$;

COMMENT ON FUNCTION fn_layer_delete IS
    'Phase E E105: 旧 0B03 を CREATE OR REPLACE で v2 化。layer_history 退避 + deleted_at / valid_to 二重書き。';
