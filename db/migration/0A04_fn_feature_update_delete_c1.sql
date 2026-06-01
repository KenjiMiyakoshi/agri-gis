-- A104: C1 修復 - fn_feature_update / fn_feature_delete の valid_from/valid_to を CURRENT_DATE で接合
-- Review② C1: 旧実装は valid_from/valid_to を据え置きで history に入れていたため、
-- AsOf 検索が正しく動作せず、AsOfTests がテスト内で手動 UPDATE で補正していた。
-- 本修復で「history は valid_to=CURRENT_DATE で閉じる、current は valid_from=CURRENT_DATE で開く」
-- 半開区間 [valid_from, valid_to) の接合を実装する。
-- 同日 UPDATE 連発時のゼロ幅区間 (valid_from=valid_to=CURRENT_DATE) は仕様で許容
-- (asOf は半開区間なので 0 件ヒット = OK)。
--
-- A105 (geom strip) で再度 CREATE OR REPLACE される予定だが、本ファイルでも C1 だけは確定させる。

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

    SELECT to_jsonb(v_cur) INTO v_before;

    -- 旧行を history へ退避。**C1 修復**: valid_to を CURRENT_DATE で閉じる
    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from,
        CURRENT_DATE,                                         -- ★ C1: 旧行を今日まで閉じる
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
        valid_from  = CURRENT_DATE,                           -- ★ C1
        updated_at  = now(),
        updated_by  = p_actor,
        version     = v_cur.version + 1
     WHERE entity_id = p_entity_id;

    new_version := v_cur.version + 1;

    SELECT to_jsonb(fc.*) INTO v_after
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


-- fn_feature_delete も同様に history の valid_to を CURRENT_DATE で閉じる
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
        v_cur.attributes_schema_version, v_cur.valid_from,
        CURRENT_DATE,                                         -- ★ C1: 削除時も valid_to を今日で閉じる
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
