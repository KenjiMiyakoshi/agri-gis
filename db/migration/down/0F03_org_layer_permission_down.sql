-- F101 rollback: org_layer_permission テーブルと index を削除
-- 0F03_org_layer_permission.sql の逆操作

DROP INDEX IF EXISTS ix_org_layer_perm_view;
DROP INDEX IF EXISTS ix_org_layer_perm_layer;
DROP TABLE IF EXISTS org_layer_permission;
