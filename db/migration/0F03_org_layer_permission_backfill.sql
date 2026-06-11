-- F102: org_layer_permission の初期 backfill (Phase F WF1)
-- 既存 organizations × 現役 layers の全組み合わせを INSERT。
-- デフォルト:
--   - can_view = true  (既存運用との互換性: 全 org がすべての層を見れていた状態を再現)
--   - can_edit = admin role を持つユーザーを含む組織は true、それ以外は false
--
-- 採用方針: docs/org-layer-permission.md セクション 3
--
-- 注:
--   - layers の有効判定は valid_to = '9999-12-31'::date (Phase E バイテンポラル)
--   - organizations の論理削除 (deleted_at) は除外
--   - ON CONFLICT DO NOTHING で複数回実行しても安全 (運用上は単発のみ)

INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit)
SELECT
    o.id AS org_id,
    l.layer_id,
    true AS can_view,
    CASE
        WHEN EXISTS (
            SELECT 1
              FROM users u
              JOIN user_roles ur ON ur.user_id = u.user_id
             WHERE u.org_id = o.id
               AND u.deleted_at IS NULL
               AND ur.role = 'admin'
        )
        THEN true
        ELSE false
    END AS can_edit
  FROM organizations o
 CROSS JOIN layers l
 WHERE o.deleted_at IS NULL
   AND l.valid_to = '9999-12-31'::date
ON CONFLICT (org_id, layer_id) DO NOTHING;
