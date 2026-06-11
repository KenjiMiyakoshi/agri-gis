-- F102 rollback: backfill された権限行を削除
-- 注: backfill のみ削除する (UI/API 経由で追加された行も含めて全消去するため、
-- 「テーブル自体を作り直す」運用が安全。本スクリプトは開発環境向けの簡易ロールバック)

TRUNCATE TABLE org_layer_permission;
