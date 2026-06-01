-- 表示確認用シードデータ
-- 座標は EPSG:4326 で書き、ST_Transform で EPSG:3857 に変換して格納。
--
-- 前提：マイグレーション 001-005 (`db/migration/001_layers_add_schema.sql` 〜
-- `005_layer_schema_version.sql`) が適用済みであること。
-- 新規環境では `docker compose up -d` 後に手動で migration 群を流すか、
-- 001_init.sql に列定義を反映してから本ファイルを実行する必要がある
-- （0101 README の運用方針参照）。
-- migration 0106 が layer_schema_version への初期行投入を担当するため、
-- 本ファイルでは layer_schema_version への INSERT は行わない。

INSERT INTO layers (layer_name, layer_type, schema_json, schema_version)
VALUES
    (
        'サンプル圃場', 'polygon',
        '{"fields":[{"key":"name","type":"string","required":true,"label":"圃場名"},{"key":"crop","type":"string","required":false,"label":"作物"}]}'::jsonb,
        1
    ),
    (
        'サンプル観測点', 'point',
        '{"fields":[{"key":"name","type":"string","required":true,"label":"観測点名"}]}'::jsonb,
        1
    );

-- 圃場ポリゴン (北海道帯広付近の架空区画)
INSERT INTO feature_current (
    layer_id, entity_id, geom, attributes,
    created_by, updated_by, version, attributes_schema_version
)
VALUES
    (
        1, gen_random_uuid(),
        ST_Transform(
            ST_GeomFromText(
                'POLYGON((143.200 42.910, 143.205 42.910, 143.205 42.913, 143.200 42.913, 143.200 42.910))',
                4326
            ),
            3857
        ),
        '{"name": "A圃場", "crop": "じゃがいも"}'::jsonb,
        'system', 'system', 1, 1
    ),
    (
        1, gen_random_uuid(),
        ST_Transform(
            ST_GeomFromText(
                'POLYGON((143.206 42.910, 143.211 42.910, 143.211 42.913, 143.206 42.913, 143.206 42.910))',
                4326
            ),
            3857
        ),
        '{"name": "B圃場", "crop": "小麦"}'::jsonb,
        'system', 'system', 1, 1
    );

-- 観測点
INSERT INTO feature_current (
    layer_id, entity_id, geom, attributes,
    created_by, updated_by, version, attributes_schema_version
)
VALUES
    (
        2, gen_random_uuid(),
        ST_Transform(ST_GeomFromText('POINT(143.2025 42.9115)', 4326), 3857),
        '{"name": "観測点1"}'::jsonb,
        'system', 'system', 1, 1
    );
