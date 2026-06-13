-- LGP101 (Phase LG' WLGP1): layer_group の組織スコープ化 + layer_group_member 配置テーブル。
--
-- 背景 (PHASE_LG_PRIME_PLAN.md §1):
--   Phase LG WLG1 で layer_group に org_id を入れ忘れ、全組織共通の単一グローバルツリーに
--   なっていた。本 migration で組織ごとの完全独立ツリーへ修正する。
--   - layer_group.org_id を追加 → 既定組織 (code='default') へ backfill → NOT NULL 化
--   - layer_group_member(org_id, layer_id, group_id, sort_order) でツリー上の配置を持つ
--     (同一レイヤを org A は『賦課』、org B は『測量』へ置ける完全独立ツリー)
--   - 既存 layers.group_id / sort_order を既定組織 member へ移送 (以後 member が真実源)
--   - layers.group_id / sort_order は後方互換で残置 (deprecated、物理削除は LG'')
--
-- ファイル名: 0LGP01 は ordinal (StringComparer.Ordinal / LC_ALL=C sort) で 0LG01 の後に並ぶ
--   ('0LG0...' < '0LGP...': position 3 で '0'(0x30) < 'P'(0x50))。
--   PostgisContainerFixture が OrderBy(StringComparer.Ordinal) で適用するため、
--   layer_group 作成 (0LG01) の後に確実に走る。命名調整は不要。
--
-- 全冪等 (ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS / ON CONFLICT DO NOTHING)。

-- 1) layer_group.org_id を additive 追加 (まず NULL 許容)
ALTER TABLE layer_group
    ADD COLUMN IF NOT EXISTS org_id INT NULL REFERENCES organizations(id);

-- 2) 既存行を既定組織 (code='default') へ backfill してから NOT NULL 化
UPDATE layer_group
   SET org_id = (SELECT id FROM organizations WHERE code = 'default' AND deleted_at IS NULL)
 WHERE org_id IS NULL;

ALTER TABLE layer_group ALTER COLUMN org_id SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_layer_group_org ON layer_group(org_id);

-- 3) 組織×レイヤの配置テーブル (ツリー上の位置 = group_id + sort_order)
--    group_id IS NULL = そのレイヤを当該組織のルート直下に置く。
--    layer 削除で member も消える (CASCADE)。group 削除で member は SET NULL でルートへ退避。
CREATE TABLE IF NOT EXISTS layer_group_member (
    org_id     INT NOT NULL REFERENCES organizations(id),
    layer_id   INT NOT NULL REFERENCES layers(layer_id)     ON DELETE CASCADE,
    group_id   INT NULL     REFERENCES layer_group(group_id) ON DELETE SET NULL,
    sort_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (org_id, layer_id)
);

CREATE INDEX IF NOT EXISTS ix_layer_group_member_group ON layer_group_member(group_id);

-- 4) 既存 layers.group_id / sort_order を既定組織の member へ移送 (冪等)
INSERT INTO layer_group_member (org_id, layer_id, group_id, sort_order)
SELECT (SELECT id FROM organizations WHERE code = 'default' AND deleted_at IS NULL),
       layer_id, group_id, sort_order
  FROM layers
 WHERE valid_to = '9999-12-31'::date
ON CONFLICT (org_id, layer_id) DO NOTHING;

-- 5) コメント
COMMENT ON COLUMN layer_group.org_id IS
    '所有組織 (Phase LG'' WLGP1)。組織ごとに完全独立したデフォルトツリー。';
COMMENT ON TABLE layer_group_member IS
    'レイヤの組織別ツリー配置 (Phase LG'' WLGP1)。group_id NULL = 当該組織のルート直下。'
    ' layers.group_id/sort_order に代わる真実源。';
COMMENT ON COLUMN layer_group_member.group_id IS
    '所属グループ (NULL = ルート直下)。group 削除時は ON DELETE SET NULL でルートへ退避';
COMMENT ON COLUMN layer_group_member.sort_order IS '同一親 (グループ or ルート) 内の表示順 (昇順)';

-- 6) 旧列を deprecated として明示 (残置、物理削除は Phase LG'')
COMMENT ON COLUMN layers.group_id IS
    '[DEPRECATED Phase LG''] 旧グローバルツリーの所属。真実源は layer_group_member。物理削除は LG''';
COMMENT ON COLUMN layers.sort_order IS
    '[DEPRECATED Phase LG''] 旧グローバルツリーの表示順。真実源は layer_group_member。物理削除は LG''';
