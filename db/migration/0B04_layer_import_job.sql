-- B104 (WB1): layer_import_job テーブル
-- 同期完結のまま 1 行だけ書く観測基盤 (拡張性レビュー 補強 1)。
-- Phase B では start → bulk × N → finalize の状態遷移を記録。
-- Phase C/D で非同期化 (バックグラウンドジョブ + 進捗 polling API) への発展余地を確保。

CREATE TABLE IF NOT EXISTS layer_import_job (
    job_id          UUID         PRIMARY KEY,
    layer_id        INT          NOT NULL REFERENCES layers(layer_id) ON DELETE RESTRICT,
    status          TEXT         NOT NULL,
    total_count     INT          NULL,
    inserted_count  INT          NOT NULL DEFAULT 0,
    started_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    finished_at     TIMESTAMPTZ  NULL,
    created_by      UUID         NOT NULL REFERENCES users(user_id) ON DELETE RESTRICT,
    created_org_id  INT          NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    error_text      TEXT         NULL,
    CONSTRAINT layer_import_job_status_check
        CHECK (status IN ('running', 'succeeded', 'failed'))
);

CREATE INDEX IF NOT EXISTS ix_layer_import_job_layer_id ON layer_import_job (layer_id);
CREATE INDEX IF NOT EXISTS ix_layer_import_job_running
    ON layer_import_job (layer_id) WHERE status = 'running';

COMMENT ON TABLE  layer_import_job IS 'Phase B B104: バルク投入の観測テーブル (同期完結、1 ジョブ 1 行)';
COMMENT ON COLUMN layer_import_job.status IS 'running / succeeded / failed';
COMMENT ON COLUMN layer_import_job.total_count IS 'クライアントが宣言した予定総数 (NULL 可)';
COMMENT ON COLUMN layer_import_job.inserted_count IS 'これまでに実投入された feature 数 (chunk 毎に増加)';
COMMENT ON COLUMN layer_import_job.error_text IS 'finalize status=failed 時のエラー内容';
