-- 0110: fn_layer_schema_upsert
-- レイヤの schema_json を更新し、layer_schema_version に履歴を append。
-- 旧 layer_schema_version の最新行 (valid_to IS NULL) を now() で閉じる。
-- 他関数と揃えて p_request_id も受け取り audit_log に保存する。

CREATE OR REPLACE FUNCTION fn_layer_schema_upsert(
    p_layer_id    INT,
    p_schema_json JSONB,
    p_actor       TEXT,
    p_request_id  TEXT
) RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    v_old_version INT;
    v_new_version INT;
    v_before      JSONB;
    v_after       JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT schema_version, to_jsonb(l.*)
      INTO v_old_version, v_before
      FROM layers l
     WHERE layer_id = p_layer_id
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    v_new_version := v_old_version + 1;

    -- 旧現行行 (valid_to IS NULL) を閉じる
    UPDATE layer_schema_version
       SET valid_to = now()
     WHERE layer_id       = p_layer_id
       AND schema_version = v_old_version
       AND valid_to IS NULL;

    -- 新行を append
    INSERT INTO layer_schema_version (
        layer_id, schema_version, schema_json,
        valid_from, valid_to, created_by
    ) VALUES (
        p_layer_id, v_new_version, p_schema_json,
        now(), NULL, p_actor
    );

    -- layers 本体を更新
    UPDATE layers
       SET schema_json    = p_schema_json,
           schema_version = v_new_version
     WHERE layer_id = p_layer_id;

    SELECT to_jsonb(l.*) INTO v_after
      FROM layers l
     WHERE layer_id = p_layer_id;

    INSERT INTO audit_log (
        actor, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, 'schema_upsert', 'layers',
        p_layer_id, NULL, NULL,
        v_before, v_after, p_request_id
    );

    RETURN v_new_version;
END;
$$;
