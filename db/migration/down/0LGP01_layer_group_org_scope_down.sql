-- LGP101 rollback: layer_group_member を落とし layer_group.org_id を削除する。
-- 0LGP01_layer_group_org_scope.sql の逆操作。layers.group_id/sort_order は LG01 down が扱う。

DROP TABLE IF EXISTS layer_group_member;

DROP INDEX IF EXISTS ix_layer_group_org;

ALTER TABLE layer_group DROP COLUMN IF EXISTS org_id;
