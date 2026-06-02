-- B101 (WB1): layers テーブル拡張
-- 新規列: description, source_format, source_srid, geometry_type,
--          created_by, created_org_id, deleted_at, updated_at
-- 既存 owner_org_id は legacy として残置 (Phase A までの空欄列)。
-- created_by / created_org_id は **NULLABLE** で導入 (Phase A 0A02 と同パターン)。
-- 既存 seed admin が居れば backfill、無ければ NULL のまま (Phase B 以降で API が NOT NULL を保証)。

ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS description    TEXT        NULL,
    ADD COLUMN IF NOT EXISTS source_format  TEXT        NULL,
    ADD COLUMN IF NOT EXISTS source_srid    INT         NULL,
    ADD COLUMN IF NOT EXISTS geometry_type  TEXT        NULL,
    ADD COLUMN IF NOT EXISTS created_by     UUID        NULL,
    ADD COLUMN IF NOT EXISTS created_org_id INT         NULL,
    ADD COLUMN IF NOT EXISTS deleted_at     TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS updated_at     TIMESTAMPTZ NOT NULL DEFAULT now();

-- CHECK 制約: source_format
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.check_constraints
         WHERE constraint_name = 'layers_source_format_check'
    ) THEN
        ALTER TABLE layers ADD CONSTRAINT layers_source_format_check
            CHECK (source_format IS NULL OR source_format IN ('geojson','csv','shapefile','mif','tab'));
    END IF;
END $$;

-- CHECK 制約: geometry_type
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.check_constraints
         WHERE constraint_name = 'layers_geometry_type_check'
    ) THEN
        ALTER TABLE layers ADD CONSTRAINT layers_geometry_type_check
            CHECK (geometry_type IS NULL OR geometry_type IN
                   ('Point','LineString','Polygon',
                    'MultiPoint','MultiLineString','MultiPolygon',
                    'GeometryCollection'));
    END IF;
END $$;

-- FK: created_by → users(user_id)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
         WHERE constraint_name = 'fk_layers_created_by'
    ) THEN
        ALTER TABLE layers ADD CONSTRAINT fk_layers_created_by
            FOREIGN KEY (created_by) REFERENCES users(user_id) ON DELETE RESTRICT;
    END IF;
END $$;

-- FK: created_org_id → organizations(id)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
         WHERE constraint_name = 'fk_layers_created_org'
    ) THEN
        ALTER TABLE layers ADD CONSTRAINT fk_layers_created_org
            FOREIGN KEY (created_org_id) REFERENCES organizations(id) ON DELETE RESTRICT;
    END IF;
END $$;

-- 既存 layers の geometry_type を layer_type から推測してバックフィル
UPDATE layers
   SET geometry_type = CASE layer_type
                          WHEN 'point'      THEN 'Point'
                          WHEN 'polygon'    THEN 'Polygon'
                          WHEN 'linestring' THEN 'LineString'
                          ELSE NULL
                       END
 WHERE geometry_type IS NULL;

-- 既存 layers の created_by / created_org_id を、admin ロール持ちユーザが居れば backfill
WITH first_admin AS (
    SELECT u.user_id, u.org_id
      FROM users u
      JOIN user_roles ur ON ur.user_id = u.user_id
     WHERE ur.role = 'admin' AND u.deleted_at IS NULL
     LIMIT 1
)
UPDATE layers
   SET created_by     = COALESCE(layers.created_by, fa.user_id),
       created_org_id = COALESCE(layers.created_org_id, fa.org_id)
  FROM first_admin fa
 WHERE layers.created_by IS NULL OR layers.created_org_id IS NULL;

-- INDEX
CREATE INDEX IF NOT EXISTS ix_layers_active         ON layers (layer_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_layers_created_org_id ON layers (created_org_id);

COMMENT ON COLUMN layers.description    IS 'Phase B B101: ユーザ記述';
COMMENT ON COLUMN layers.source_format  IS 'Phase B B101: import 元形式 (geojson/csv/shapefile/mif/tab)';
COMMENT ON COLUMN layers.source_srid    IS 'Phase B B101: import 元 SRID (NULL=未指定/WGS84 想定)';
COMMENT ON COLUMN layers.geometry_type  IS 'Phase B B101: PostGIS geometry type (Point/LineString/Polygon/Multi*)';
COMMENT ON COLUMN layers.created_by     IS 'Phase B B101: 作成者 users.user_id (NULL=本 migration 適用時 admin 不在のレガシー行)';
COMMENT ON COLUMN layers.created_org_id IS 'Phase B B101: 作成組織 organizations.id (同上)';
COMMENT ON COLUMN layers.deleted_at     IS 'Phase B B101: 論理削除タイムスタンプ (NULL=active)';
COMMENT ON COLUMN layers.updated_at     IS 'Phase B B101: 更新時刻 (now() default)';
