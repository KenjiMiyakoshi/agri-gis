-- E104 (WE1): fn_layer_update
-- PATCH /api/admin/layers/{id} の関数化。Phase A fn_feature_update (007/0A04) 流儀踏襲。
-- 楽観ロック (expected_version) + history 退避 + audit_log。

CREATE OR REPLACE FUNCTION fn_layer_update(
    p_layer_id          INT,
    p_layer_name        TEXT,
    p_layer_type        TEXT,
    p_geometry_type     TEXT,
    p_description       TEXT,
    p_source_format     TEXT,
    p_source_srid       INT,
    p_expected_version  INT,
    p_actor             TEXT,
    p_request_id        TEXT,
    p_user_id           UUID,
    p_org_id            INT
)
RETURNS TABLE(layer_id INT, version INT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_before_doc  JSONB;
    v_after_doc   JSONB;
    v_current_row layers%ROWTYPE;
    v_new_version INT;
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

    -- active な layer 行を楽観ロックで取得
    SELECT * INTO v_current_row
      FROM layers
     WHERE layers.layer_id = p_layer_id
       AND valid_to = '9999-12-31'::date
       AND deleted_at IS NULL
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    IF v_current_row.version <> p_expected_version THEN
        RAISE EXCEPTION 'optimistic lock violation: layer % expected version %, got %',
            p_layer_id, p_expected_version, v_current_row.version
            USING ERRCODE = 'P0001';
    END IF;

    v_before_doc := to_jsonb(v_current_row);

    -- 旧行を layer_history に退避 (valid_to = CURRENT_DATE で半開区間を閉じる)
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
        p_actor, 'update'
    );

    v_new_version := p_expected_version + 1;

    -- layers 本体を更新 (新版を [CURRENT_DATE, 9999-12-31) で active 化)
    UPDATE layers
       SET layer_name    = COALESCE(p_layer_name,    layer_name),
           layer_type    = COALESCE(p_layer_type,    layer_type),
           geometry_type = COALESCE(p_geometry_type, geometry_type),
           description   = COALESCE(p_description,   description),
           source_format = COALESCE(p_source_format, source_format),
           source_srid   = COALESCE(p_source_srid,   source_srid),
           version       = v_new_version,
           valid_from    = CURRENT_DATE,
           valid_to      = '9999-12-31'::date,
           updated_at    = now()
     WHERE layers.layer_id = p_layer_id;

    SELECT to_jsonb(l.*) INTO v_after_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id;

    -- audit_log 記録 (Phase A C2 修復済の actor_user_id NOT NULL を満たす)
    -- before/after_doc に version 列含むので追加 meta は不要
    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'layer_update', 'layers',
        p_layer_id, NULL, NULL,
        v_before_doc, v_after_doc, p_request_id
    );

    RETURN QUERY SELECT p_layer_id, v_new_version;
END;
$$;

COMMENT ON FUNCTION fn_layer_update IS
    'Phase E E104: PATCH /api/admin/layers/{id} 用。楽観ロック + layer_history 退避 + audit_log。Phase A fn_feature_update 流儀踏襲。';
