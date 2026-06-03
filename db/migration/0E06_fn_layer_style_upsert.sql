-- E106 (WE1): fn_layer_style_upsert
-- PUT /api/admin/layers/{id}/style の関数化。Phase A fn_layer_schema_upsert (009) 流儀踏襲。
-- layer_style_version に append + 旧 active 行を CURRENT_DATE で閉じる + layers.style_json 同期更新。

CREATE OR REPLACE FUNCTION fn_layer_style_upsert(
    p_layer_id    INT,
    p_style_json  JSONB,
    p_actor       TEXT,
    p_request_id  TEXT,
    p_user_id     UUID,
    p_org_id      INT
)
RETURNS TABLE(layer_id INT, style_version INT)
LANGUAGE plpgsql
AS $$
DECLARE
    v_old_version INT;
    v_new_version INT;
    v_before_doc  JSONB;
    v_after_doc   JSONB;
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

    -- active layer の存在確認 + before snapshot
    SELECT to_jsonb(l.*) INTO v_before_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id
       AND l.valid_to = '9999-12-31'::date
       AND l.deleted_at IS NULL
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    -- 現在 active な style_version を取得 (なければ 0)
    SELECT COALESCE(MAX(lsv.style_version), 0) INTO v_old_version
      FROM layer_style_version lsv
     WHERE lsv.layer_id = p_layer_id;

    v_new_version := v_old_version + 1;

    -- 旧 active 行を CURRENT_DATE で閉じる
    UPDATE layer_style_version
       SET valid_to = CURRENT_DATE
     WHERE layer_style_version.layer_id = p_layer_id
       AND layer_style_version.valid_to = '9999-12-31'::date;

    -- 新版を append
    INSERT INTO layer_style_version (
        layer_id, style_version, style_json,
        valid_from, valid_to, created_by, created_at
    ) VALUES (
        p_layer_id, v_new_version, p_style_json,
        CURRENT_DATE, '9999-12-31'::date, p_user_id, now()
    );

    -- layers.style_json も同期更新 (current value 冗長保持、SELECT の高速化のため)
    UPDATE layers
       SET style_json = p_style_json,
           updated_at = now()
     WHERE layers.layer_id = p_layer_id;

    SELECT to_jsonb(l.*) INTO v_after_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id;

    -- audit_log: before/after_doc に style_json + version 列が含まれるので追加 meta 不要
    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'layer_style_upsert', 'layers',
        p_layer_id, NULL, NULL,
        v_before_doc, v_after_doc, p_request_id
    );

    RETURN QUERY SELECT p_layer_id, v_new_version;
END;
$$;

COMMENT ON FUNCTION fn_layer_style_upsert IS
    'Phase E E106: PUT /api/admin/layers/{id}/style 用。layer_style_version append + layers.style_json 同期 + audit_log。Phase A fn_layer_schema_upsert 流儀踏襲。';
