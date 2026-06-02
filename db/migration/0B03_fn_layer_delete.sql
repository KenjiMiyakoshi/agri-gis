-- B103 (WB1): fn_layer_delete
-- layers.deleted_at を立てるだけ。feature_current / feature_history は据え置き (案 C 論点 5)。
-- FeatureEndpoints 側で deleted_at IS NULL フィルタを強制する (B205)。
-- 既削除レイヤへの再削除は 02000 (no_data)。

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
    v_before_doc JSONB;
    v_after_doc  JSONB;
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

    SELECT to_jsonb(l.*) INTO v_before_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id AND l.deleted_at IS NULL
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    UPDATE layers
       SET deleted_at = now(),
           updated_at = now()
     WHERE layer_id = p_layer_id;

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

COMMENT ON FUNCTION fn_layer_delete IS 'Phase B B103: layers.deleted_at を立てる論理削除。feature_current は据え置き';
