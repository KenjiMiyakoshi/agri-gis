-- LG101: layer_group テーブル + layers.group_id / layers.sort_order (Phase LG WLG1)
-- レイヤグループ (フォルダ概念)。組織デフォルトツリーを admin が管理する。
--
-- 設計 (PHASE_LG_PLAN.md §1):
--   - parent_group_id 自己参照で多階層 OK。循環防止は API レベル検証 (DB trigger は過剰)
--   - グループは presentation metadata でありバイテンポラル対象外 (layer_history に焼かない)
--   - グループ削除: 子グループは CASCADE、所属レイヤは ON DELETE SET NULL でルート直下へ退避
--   - 監査: admin CRUD endpoint の Tx 内で audit_log に直接 INSERT (action='layer_group_*')

CREATE TABLE IF NOT EXISTS layer_group (
    group_id        SERIAL PRIMARY KEY,
    parent_group_id INT  NULL REFERENCES layer_group(group_id) ON DELETE CASCADE,
    group_name      TEXT NOT NULL CHECK (length(group_name) BETWEEN 1 AND 100),
    sort_order      INT  NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_layer_group_parent ON layer_group(parent_group_id);

-- layers にグループ所属 + 同一親内の表示順を追加 (additive、既存行は group_id NULL = ルート直下)
ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS group_id   INT NULL REFERENCES layer_group(group_id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS sort_order INT NOT NULL DEFAULT 0;

COMMENT ON TABLE layer_group IS
    'レイヤグループ (Phase LG WLG1)。組織デフォルトツリー。presentation metadata でバイテンポラル対象外';
COMMENT ON COLUMN layer_group.parent_group_id IS
    '親グループ (NULL = ルート直下)。自己参照、多階層 OK。循環防止は API レベル検証';
COMMENT ON COLUMN layer_group.sort_order IS '同一親内の表示順 (昇順)';
COMMENT ON COLUMN layers.group_id IS
    '所属レイヤグループ (NULL = ルート直下)。グループ削除時は SET NULL でルートへ退避';
COMMENT ON COLUMN layers.sort_order IS '同一親 (グループ or ルート) 内の表示順 (昇順)';
