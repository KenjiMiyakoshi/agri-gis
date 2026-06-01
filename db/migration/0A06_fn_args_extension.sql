-- A106: PL/pgSQL 4関数の引数を p_user_id UUID, p_org_id INT で拡張
-- 既存呼び出しは DEFAULT NULL で互換維持。API 側は A206 で渡すよう書き換える。
--
-- 同時に:
--   - audit_log に actor_org_id INT NULL (FK organizations) を追加
--   - 関数内 INSERT で actor_user_id / actor_org_id を埋める
--   - 末尾で audit_log.actor_user_id を NOT NULL 化（NULL 行を削除した後）

-- 1. audit_log に actor_org_id 列を追加
ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS actor_org_id INT;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
         WHERE constraint_name = 'fk_audit_log_org'
           AND table_name = 'audit_log'
    ) THEN
        ALTER TABLE audit_log
            ADD CONSTRAINT fk_audit_log_org
            FOREIGN KEY (actor_org_id) REFERENCES organizations(id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_audit_log_actor_org_id ON audit_log(actor_org_id);

COMMENT ON COLUMN audit_log.actor_org_id IS
    'FK to organizations.org_id; org snapshot at audit time (NULL allowed for backward compat)';


-- 2. fn_feature_insert: 引数末尾に p_user_id UUID, p_org_id INT を追加（DEFAULT NULL で互換）
CREATE OR REPLACE FUNCTION fn_feature_insert(
    p_layer_id          INT,
    p_entity_id         UUID,
    p_geom_geojson_4326 TEXT,
    p_attributes        JSONB,
    p_actor             TEXT,
    p_request_id        TEXT,
    p_user_id           UUID DEFAULT NULL,
    p_org_id            INT  DEFAULT NULL
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

    SELECT to_jsonb(fc.*) - 'geom'
           || jsonb_build_object('geom_geojson',
                CASE WHEN fc.geom IS NULL THEN NULL
                     ELSE ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::jsonb
                END)
      INTO v_after_doc
      FROM feature_current fc
     WHERE feature_id = v_feature_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'feature_insert', 'feature_current',
        p_layer_id, p_entity_id, v_feature_id,
        NULL, v_after_doc, p_request_id
    );

    RETURN v_feature_id;
END;
$$;


-- 3. fn_feature_update: 引数末尾に p_user_id UUID, p_org_id INT 追加
CREATE OR REPLACE FUNCTION fn_feature_update(
    p_entity_id             UUID,
    p_new_geom_geojson_4326 TEXT,
    p_new_attributes        JSONB,
    p_actor                 TEXT,
    p_expected_version      INT,
    p_request_id            TEXT,
    p_user_id               UUID DEFAULT NULL,
    p_org_id                INT  DEFAULT NULL,
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

    v_before := to_jsonb(v_cur) - 'geom'
                || jsonb_build_object('geom_geojson',
                     CASE WHEN v_cur.geom IS NULL THEN NULL
                          ELSE ST_AsGeoJSON(ST_Transform(v_cur.geom, 4326))::jsonb
                     END);

    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from,
        CURRENT_DATE,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'update'
    );

    IF p_new_geom_geojson_4326 IS NOT NULL THEN
        v_new_geom := ST_Transform(
                        ST_SetSRID(ST_GeomFromGeoJSON(p_new_geom_geojson_4326), 4326),
                        3857
                      );
    END IF;

    UPDATE feature_current SET
        geom        = COALESCE(v_new_geom, geom),
        attributes  = COALESCE(p_new_attributes, attributes),
        valid_from  = CURRENT_DATE,
        updated_at  = now(),
        updated_by  = p_actor,
        version     = v_cur.version + 1
     WHERE entity_id = p_entity_id;

    new_version := v_cur.version + 1;

    SELECT to_jsonb(fc.*) - 'geom'
           || jsonb_build_object('geom_geojson',
                CASE WHEN fc.geom IS NULL THEN NULL
                     ELSE ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::jsonb
                END)
      INTO v_after
      FROM feature_current fc
     WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'feature_update', 'feature_current',
        v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
        v_before, v_after, p_request_id
    );
END;
$$;


-- 4. fn_feature_delete: 引数末尾に p_user_id UUID, p_org_id INT 追加
CREATE OR REPLACE FUNCTION fn_feature_delete(
    p_entity_id  UUID,
    p_actor      TEXT,
    p_request_id TEXT,
    p_user_id    UUID DEFAULT NULL,
    p_org_id     INT  DEFAULT NULL
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

    v_before := to_jsonb(v_cur) - 'geom'
                || jsonb_build_object('geom_geojson',
                     CASE WHEN v_cur.geom IS NULL THEN NULL
                          ELSE ST_AsGeoJSON(ST_Transform(v_cur.geom, 4326))::jsonb
                     END);

    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from,
        CURRENT_DATE,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'delete'
    );

    DELETE FROM feature_current WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'feature_delete', 'feature_current',
        v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
        v_before, NULL, p_request_id
    );
END;
$$;


-- 5. fn_layer_schema_upsert: 引数末尾に p_user_id UUID, p_org_id INT 追加
CREATE OR REPLACE FUNCTION fn_layer_schema_upsert(
    p_layer_id    INT,
    p_schema_json JSONB,
    p_actor       TEXT,
    p_request_id  TEXT,
    p_user_id     UUID DEFAULT NULL,
    p_org_id      INT  DEFAULT NULL
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

    SELECT schema_version, to_jsonb(l.*) - 'geom'
      INTO v_old_version, v_before
      FROM layers l
     WHERE layer_id = p_layer_id
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    v_new_version := v_old_version + 1;

    UPDATE layer_schema_version
       SET valid_to = now()
     WHERE layer_id       = p_layer_id
       AND schema_version = v_old_version
       AND valid_to IS NULL;

    INSERT INTO layer_schema_version (
        layer_id, schema_version, schema_json,
        valid_from, valid_to, created_by
    ) VALUES (
        p_layer_id, v_new_version, p_schema_json,
        now(), NULL, p_actor
    );

    UPDATE layers
       SET schema_json    = p_schema_json,
           schema_version = v_new_version
     WHERE layer_id = p_layer_id;

    SELECT to_jsonb(l.*) - 'geom' INTO v_after
      FROM layers l
     WHERE layer_id = p_layer_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id, 'schema_upsert', 'layers',
        p_layer_id, NULL, NULL,
        v_before, v_after, p_request_id
    );

    RETURN v_new_version;
END;
$$;


-- 6. actor_user_id NOT NULL 化
-- ※ ここまでに行が残っていれば NULL のはず（A102 のバックフィルで埋まらなかった行）。
-- 開発・テスト環境のみ想定: NULL 行を消してから NOT NULL を適用する。
-- COMMENT は A102 で書き換え済みだが、NOT NULL 化に合わせて更新。
DELETE FROM audit_log WHERE actor_user_id IS NULL;

ALTER TABLE audit_log ALTER COLUMN actor_user_id SET NOT NULL;

COMMENT ON COLUMN audit_log.actor_user_id IS
    'FK to users.user_id; canonical actor identity (NOT NULL since A106)';
