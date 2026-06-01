-- 0109: fn_feature_delete (履歴退避 → 物理削除)
-- archived_reason='delete' で feature_history に退避してから feature_current から DELETE。
-- 削除後も history と audit_log から完全に追跡可能。

CREATE OR REPLACE FUNCTION fn_feature_delete(
    p_entity_id  UUID,
    p_actor      TEXT,
    p_request_id TEXT
) RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_cur    feature_current%ROWTYPE;
    v_before JSONB;
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

    SELECT to_jsonb(v_cur) INTO v_before;

    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from, v_cur.valid_to,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'delete'
    );

    DELETE FROM feature_current WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (
        actor, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, 'feature_delete', 'feature_current',
        v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
        v_before, NULL, p_request_id
    );
END;
$$;
