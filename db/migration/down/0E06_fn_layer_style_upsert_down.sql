-- E106 (WE1) ロールバック: fn_layer_style_upsert 削除

DROP FUNCTION IF EXISTS fn_layer_style_upsert(INT, JSONB, TEXT, TEXT, UUID, INT);
