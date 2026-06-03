-- E'102 + E'103 (WE'1): layers.deleted_at 列の完全削除 + 3 関数の v3 化
-- Phase E では fn_layer_delete v2 で deleted_at と valid_to を二重書きにして
-- 後方互換のため列を残した。Phase E' で削除し、削除判定は valid_to <> '9999-12-31' のみに統一。
--
-- 関数 v3 化 (CREATE OR REPLACE):
--   - fn_layer_delete: deleted_at = now() 操作を削除
--   - fn_layer_update: WHERE 条件から AND deleted_at IS NULL を削除
--   - fn_layer_style_upsert: 同上

-- ============================================================
-- 1. fn_layer_delete v3: deleted_at 操作削除
-- ============================================================
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
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    v_before_doc := to_jsonb(v_current_row);

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

    -- E'103: deleted_at 操作を削除、valid_to のみで論理削除
    UPDATE layers
       SET valid_to   = CURRENT_DATE,
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

-- ============================================================
-- 2. fn_layer_update v3: WHERE 条件から deleted_at IS NULL 削除
-- ============================================================
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

    SELECT * INTO v_current_row
      FROM layers
     WHERE layers.layer_id = p_layer_id
       AND valid_to = '9999-12-31'::date
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

-- ============================================================
-- 3. fn_layer_style_upsert v2: WHERE 条件から deleted_at IS NULL 削除
-- ============================================================
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

    -- E'103: WHERE 条件から AND deleted_at IS NULL を削除
    SELECT to_jsonb(l.*) INTO v_before_doc
      FROM layers l
     WHERE l.layer_id = p_layer_id
       AND l.valid_to = '9999-12-31'::date
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found or already deleted: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    SELECT COALESCE(MAX(lsv.style_version), 0) INTO v_old_version
      FROM layer_style_version lsv
     WHERE lsv.layer_id = p_layer_id;

    v_new_version := v_old_version + 1;

    UPDATE layer_style_version
       SET valid_to = CURRENT_DATE
     WHERE layer_style_version.layer_id = p_layer_id
       AND layer_style_version.valid_to = '9999-12-31'::date;

    INSERT INTO layer_style_version (
        layer_id, style_version, style_json,
        valid_from, valid_to, created_by, created_at
    ) VALUES (
        p_layer_id, v_new_version, p_style_json,
        CURRENT_DATE, '9999-12-31'::date, p_user_id, now()
    );

    UPDATE layers
       SET style_json = p_style_json,
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
        p_actor, p_user_id, p_org_id, 'layer_style_upsert', 'layers',
        p_layer_id, NULL, NULL,
        v_before_doc, v_after_doc, p_request_id
    );

    RETURN QUERY SELECT p_layer_id, v_new_version;
END;
$$;

-- ============================================================
-- 4. layers.deleted_at 列の DROP
-- ============================================================
ALTER TABLE layers DROP COLUMN IF EXISTS deleted_at;

COMMENT ON TABLE layers IS
    'Phase E'' E''102: deleted_at 列 DROP。削除済 layer は valid_to <> ''9999-12-31''::date で判定。';
COMMENT ON FUNCTION fn_layer_delete IS
    'Phase E'' E''103: v3 - deleted_at 操作削除、valid_to のみで論理削除。';
COMMENT ON FUNCTION fn_layer_update IS
    'Phase E'' E''103: v3 - WHERE 条件から deleted_at IS NULL 削除。';
COMMENT ON FUNCTION fn_layer_style_upsert IS
    'Phase E'' E''103: v2 - WHERE 条件から deleted_at IS NULL 削除。';
