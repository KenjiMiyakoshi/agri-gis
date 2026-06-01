-- A105: C2 修復 - 4関数の to_jsonb(...) から geom を抜き、geom_geojson を追加
-- Review② C2: 旧実装は audit_log.before_doc / after_doc に PostGIS の geom
-- (bytea EWKB) を to_jsonb で焼き込み、hex 文字列としてテーブルが肥大化していた。
-- 修復: geom 列を抜き、ST_AsGeoJSON(ST_Transform(geom,4326)) を geom_geojson キーで
-- JSONB に埋め込む。NULL geom は NULL のまま (CASE WHEN で防御)。
--
-- A104 で C1 修復した valid_from/valid_to も保持。
-- 4関数全て CREATE OR REPLACE で更新。

-- ===== fn_feature_insert (C2 のみ、C1 影響なし) =====
CREATE OR REPLACE FUNCTION fn_feature_insert(
    p_layer_id          INT,
    p_entity_id         UUID,
    p_geom_geojson_4326 TEXT,
    p_attributes        JSONB,
    p_actor             TEXT,
    p_request_id        TEXT
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

    -- ★ C2: to_jsonb から geom を抜き geom_geojson を追加
    SELECT to_jsonb(fc.*) - 'geom'
           || jsonb_build_object('geom_geojson',
                CASE WHEN fc.geom IS NULL THEN NULL
                     ELSE ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::jsonb
                END)
      INTO v_after_doc
      FROM feature_current fc
     WHERE feature_id = v_feature_id;

    INSERT INTO audit_log (
        actor, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, 'feature_insert', 'feature_current',
        p_layer_id, p_entity_id, v_feature_id,
        NULL, v_after_doc, p_request_id
    );

    RETURN v_feature_id;
END;
$$;


-- ===== fn_feature_update (C1 + C2) =====
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

    -- ★ C2: v_before も geom を抜く (v_cur 経由なので一度 to_jsonb してから操作)
    v_before := to_jsonb(v_cur) - 'geom'
                || jsonb_build_object('geom_geojson',
                     CASE WHEN v_cur.geom IS NULL THEN NULL
                          ELSE ST_AsGeoJSON(ST_Transform(v_cur.geom, 4326))::jsonb
                     END);

    -- ★ C1: 旧行を history へ退避、valid_to = CURRENT_DATE で閉じる
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

    -- ★ C1: 新 current は valid_from = CURRENT_DATE で開く
    UPDATE feature_current SET
        geom        = COALESCE(v_new_geom, geom),
        attributes  = COALESCE(p_new_attributes, attributes),
        valid_from  = CURRENT_DATE,
        updated_at  = now(),
        updated_by  = p_actor,
        version     = v_cur.version + 1
     WHERE entity_id = p_entity_id;

    new_version := v_cur.version + 1;

    -- ★ C2: v_after も geom を抜く
    SELECT to_jsonb(fc.*) - 'geom'
           || jsonb_build_object('geom_geojson',
                CASE WHEN fc.geom IS NULL THEN NULL
                     ELSE ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::jsonb
                END)
      INTO v_after
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


-- ===== fn_feature_delete (C1 + C2) =====
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

    -- ★ C2: v_before も geom を抜く
    v_before := to_jsonb(v_cur) - 'geom'
                || jsonb_build_object('geom_geojson',
                     CASE WHEN v_cur.geom IS NULL THEN NULL
                          ELSE ST_AsGeoJSON(ST_Transform(v_cur.geom, 4326))::jsonb
                     END);

    -- ★ C1: history に退避、valid_to = CURRENT_DATE で閉じる
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


-- ===== fn_layer_schema_upsert (C2 のみ、layers に geom なしで実質 no-op だが統一) =====
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

    -- layers は geom 列がないので - 'geom' は no-op だが、4関数で書き味を統一
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
