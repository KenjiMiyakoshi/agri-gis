-- F101: org_layer_permission テーブル (Phase F WF1)
-- 組織 (organizations) × レイヤ (layers) の閲覧/編集権限を管理する。
-- 採用方針: docs/org-layer-permission.md セクション 1 / 2
--   - バイテンポラル無し (権限は現時点のみ、過去状態は audit_log で監査)
--   - CHECK 制約 NOT (can_edit AND NOT can_view) で edit は view を含意
--   - ON DELETE CASCADE (organizations / layers 削除で権限行も連動消去)
--
-- 依存: 0A01 (organizations), 0E01-0E02 (layers バイテンポラル化)
--
-- 参考: docs/phase-f-migration-numbering.md (0F03 採用理由)

CREATE TABLE IF NOT EXISTS org_layer_permission (
    org_id     INTEGER     NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    layer_id   INTEGER     NOT NULL REFERENCES layers(layer_id)  ON DELETE CASCADE,
    can_view   BOOLEAN     NOT NULL DEFAULT false,
    can_edit   BOOLEAN     NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (org_id, layer_id),
    CONSTRAINT chk_org_layer_perm_edit_implies_view
        CHECK (NOT (can_edit AND NOT can_view))
);

-- 逆引き (layer_id → 対象組織一覧、 layer 削除時の整理に使う)
CREATE INDEX IF NOT EXISTS ix_org_layer_perm_layer
    ON org_layer_permission(layer_id);

-- 主用途: GET /api/layers の org フィルタ。can_view=true のみ参照するので部分 index
CREATE INDEX IF NOT EXISTS ix_org_layer_perm_view
    ON org_layer_permission(org_id) WHERE can_view;

COMMENT ON TABLE  org_layer_permission IS
    '組織×レイヤの閲覧/編集権限 (Phase F WF1)。バイテンポラル無し、現時点のみ評価。';
COMMENT ON COLUMN org_layer_permission.can_view IS '閲覧可。GET /api/layers と /tiles で参照';
COMMENT ON COLUMN org_layer_permission.can_edit IS '編集可。POST/PATCH/DELETE /api/features で参照。can_view=false 時は不可 (CHECK 制約)';
