-- LG101 rollback: layers.group_id / layers.sort_order を削除し layer_group を落とす
ALTER TABLE layers
    DROP COLUMN IF EXISTS group_id,
    DROP COLUMN IF EXISTS sort_order;

DROP TABLE IF EXISTS layer_group;
