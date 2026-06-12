-- F103: fn_org_layer_perm_upsert 関数 (Phase F WF1)
-- 組織×レイヤ権限の upsert + audit_log 記録
-- 採用方針: docs/org-layer-permission.md セクション 4
--
-- 依存: 0F03_org_layer_permission.sql (テーブル), 0A06 (audit_log.actor_org_id 列)
--
-- 引数規約 (Phase A/E 系の他関数と統一):
--   p_actor      : display_name snapshot (audit_log.actor)
--   p_request_id : 相関 ID
--   p_user_id    : 操作者 UUID (audit_log.actor_user_id, NULL 不可だが暫定 NULL 許容)
--   p_org_id_act : 操作者所属 org_id (audit_log.actor_org_id)
-- ※ p_org_id_act は権限テーブル側の対象 org_id (p_org_id) と区別
--
-- Phase F WF2 hotfix: alphabetic loading order (Testcontainers fresh init) では本ファイル
-- (`0F03_fn_*`) がテーブル定義 (`0F03_org_layer_permission.sql`) より前に走るため、
-- CREATE FUNCTION の body 検証時にテーブルが未存在で 42P01 になる。
-- check_function_bodies を一時的に false にして body 検証を defer する (実行時に解決)。
SET check_function_bodies = false;

CREATE OR REPLACE FUNCTION fn_org_layer_perm_upsert(
    p_org_id     INT,
    p_layer_id   INT,
    p_can_view   BOOLEAN,
    p_can_edit   BOOLEAN,
    p_actor      TEXT,
    p_request_id TEXT,
    p_user_id    UUID DEFAULT NULL,
    p_org_id_act INT  DEFAULT NULL
) RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_before JSONB;
    v_after  JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    -- CHECK 制約の事前検査 (UI 側で補正済の前提だが二重防御)
    IF p_can_edit AND NOT p_can_view THEN
        RAISE EXCEPTION 'can_edit requires can_view' USING ERRCODE = '23514';
    END IF;

    SELECT to_jsonb(p.*) INTO v_before
      FROM org_layer_permission p
     WHERE p.org_id = p_org_id AND p.layer_id = p_layer_id;

    INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit, updated_at)
    VALUES (p_org_id, p_layer_id, p_can_view, p_can_edit, now())
    ON CONFLICT (org_id, layer_id)
    DO UPDATE SET
        can_view   = EXCLUDED.can_view,
        can_edit   = EXCLUDED.can_edit,
        updated_at = now();

    SELECT to_jsonb(p.*) INTO v_after
      FROM org_layer_permission p
     WHERE p.org_id = p_org_id AND p.layer_id = p_layer_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id_act, 'org_layer_perm_upsert', 'org_layer_permission',
        p_layer_id, NULL, NULL,
        v_before, v_after, p_request_id
    );
END;
$$;

COMMENT ON FUNCTION fn_org_layer_perm_upsert(INT, INT, BOOLEAN, BOOLEAN, TEXT, TEXT, UUID, INT) IS
    '組織×レイヤ権限の upsert + audit_log 記録 (Phase F WF1)';
