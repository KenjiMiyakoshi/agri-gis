-- 表示確認用シードデータ
-- 座標は EPSG:4326 で書き、ST_Transform で EPSG:3857 に変換して格納

INSERT INTO layers (layer_name, layer_type)
VALUES
    ('サンプル圃場', 'polygon'),
    ('サンプル観測点', 'point');

-- 圃場ポリゴン (北海道帯広付近の架空区画)
INSERT INTO feature_current (layer_id, entity_id, geom, attributes)
VALUES
    (
        1,
        gen_random_uuid(),
        ST_Transform(
            ST_GeomFromText(
                'POLYGON((143.200 42.910, 143.205 42.910, 143.205 42.913, 143.200 42.913, 143.200 42.910))',
                4326
            ),
            3857
        ),
        '{"name": "A圃場", "crop": "じゃがいも"}'::jsonb
    ),
    (
        1,
        gen_random_uuid(),
        ST_Transform(
            ST_GeomFromText(
                'POLYGON((143.206 42.910, 143.211 42.910, 143.211 42.913, 143.206 42.913, 143.206 42.910))',
                4326
            ),
            3857
        ),
        '{"name": "B圃場", "crop": "小麦"}'::jsonb
    );

-- 観測点
INSERT INTO feature_current (layer_id, entity_id, geom, attributes)
VALUES
    (
        2,
        gen_random_uuid(),
        ST_Transform(ST_GeomFromText('POINT(143.2025 42.9115)', 4326), 3857),
        '{"name": "観測点1"}'::jsonb
    );
