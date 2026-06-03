-- D'103 (WD'1): fn_feature_batch_update
-- POST /api/features:batch の関数化。Phase A fn_feature_update (007 + 0A06) の batch 拡張。
-- entity_ids[] × if_match_versions[] を all-or-nothing で楽観ロック検証 → 全件成功で履歴退避 + 属性 patch + audit_log。
-- 1 件でも version mismatch があれば全件 rollback (RAISE EXCEPTION → トランザクション破棄)。
-- geometry は触らない (属性のみ patch、JSONB merge `||` で適用)。

CREATE OR REPLACE FUNCTION fn_feature_batch_update(
    p_entity_ids        UUID[],
    p_if_match_versions INT[],
    p_attributes_patch  JSONB,
    p_actor             TEXT,
    p_request_id        TEXT,
    p_user_id           UUID,
    p_org_id            INT
)
RETURNS TABLE(out_entity_id UUID, out_new_version INT, out_valid_from DATE)
LANGUAGE plpgsql
AS $$
DECLARE
    v_idx          INT;
    v_eid          UUID;
    v_expected     INT;
    v_actual       INT;
    v_mismatch_ids UUID[] := ARRAY[]::UUID[];
    v_cur          feature_current%ROWTYPE;
    v_before       JSONB;
    v_after        JSONB;
BEGIN
    -- 1. 入力検証
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;
    IF p_attributes_patch IS NULL THEN
        RAISE EXCEPTION 'attributes_patch is required' USING ERRCODE = '22023';
    END IF;
    IF p_user_id IS NULL THEN
        RAISE EXCEPTION 'user_id is required' USING ERRCODE = '22023';
    END IF;
    IF p_org_id IS NULL THEN
        RAISE EXCEPTION 'org_id is required' USING ERRCODE = '22023';
    END IF;
    IF p_entity_ids IS NULL OR array_length(p_entity_ids, 1) IS NULL THEN
        RAISE EXCEPTION 'entity_ids cannot be empty' USING ERRCODE = '22023';
    END IF;
    IF p_if_match_versions IS NULL
       OR array_length(p_entity_ids, 1) <> array_length(p_if_match_versions, 1) THEN
        RAISE EXCEPTION 'entity_ids and if_match_versions must have same length'
            USING ERRCODE = '22023';
    END IF;

    -- 2. 全件突合 (1 件でも mismatch → 全件失敗、行ロックは FOR UPDATE で保持)
    FOR v_idx IN 1..array_length(p_entity_ids, 1) LOOP
        v_eid := p_entity_ids[v_idx];
        v_expected := p_if_match_versions[v_idx];
        SELECT version INTO v_actual
          FROM feature_current
         WHERE feature_current.entity_id = v_eid
           FOR UPDATE;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'entity not found: %', v_eid USING ERRCODE = '02000';
        END IF;
        IF v_actual <> v_expected THEN
            v_mismatch_ids := array_append(v_mismatch_ids, v_eid);
        END IF;
    END LOOP;

    IF array_length(v_mismatch_ids, 1) > 0 THEN
        RAISE EXCEPTION 'optimistic_lock_failed: %', v_mismatch_ids
            USING ERRCODE = 'P0001';
    END IF;

    -- 3. 全件 update + history append + audit_log
    FOR v_idx IN 1..array_length(p_entity_ids, 1) LOOP
        v_eid := p_entity_ids[v_idx];

        SELECT * INTO v_cur FROM feature_current WHERE feature_current.entity_id = v_eid;

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
            now(), p_actor, 'batch_update'
        );

        UPDATE feature_current SET
            attributes  = COALESCE(attributes, '{}'::jsonb) || p_attributes_patch,
            valid_from  = CURRENT_DATE,
            updated_at  = now(),
            updated_by  = p_actor,
            version     = v_cur.version + 1
         WHERE feature_current.entity_id = v_eid;

        SELECT to_jsonb(fc.*) - 'geom'
               || jsonb_build_object('geom_geojson',
                    CASE WHEN fc.geom IS NULL THEN NULL
                         ELSE ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::jsonb
                    END)
          INTO v_after
          FROM feature_current fc
         WHERE fc.entity_id = v_eid;

        INSERT INTO audit_log (
            actor, actor_user_id, actor_org_id, action, target_table,
            layer_id, entity_id, feature_id,
            before_doc, after_doc, request_id
        ) VALUES (
            p_actor, p_user_id, p_org_id, 'feature_batch_update', 'feature_current',
            v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
            v_before, v_after, p_request_id
        );

        out_entity_id := v_eid;
        out_new_version := v_cur.version + 1;
        out_valid_from := CURRENT_DATE;
        RETURN NEXT;
    END LOOP;
END;
$$;

COMMENT ON FUNCTION fn_feature_batch_update IS
    'Phase D-prime D''103: POST /api/features:batch 用。N 件まとめて属性 patch + 楽観ロック (all-or-nothing) + audit_log。geometry は不変。';
