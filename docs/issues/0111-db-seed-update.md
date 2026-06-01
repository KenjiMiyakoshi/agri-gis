# 0111: 既存シード (`002_seed.sql`) の新スキーマ対応

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0102, 0103, 0106 |
| Blocks | なし |

## 概要
既存の `db/init/002_seed.sql` を新スキーマ（`schema_json`, `created_by`, `version`, `attributes_schema_version`）に追従させる。

## 背景・目的
案 B' の新カラム導入後、ボリュームを作り直すと既存シードが古い形式のままで失敗する。表示確認用なので深い意味はないが、`docker compose down -v && up -d` で再現可能に保つ。

## スコープ
### 含む
- `db/init/002_seed.sql` を新スキーマに合わせて修正
- `layers` 行に `schema_json` を最低限定義
  - サンプル圃場: `{"fields":[{"key":"name","type":"string","required":true},{"key":"crop","type":"string","required":false}]}`
  - サンプル観測点: `{"fields":[{"key":"name","type":"string","required":true}]}`
- `feature_current` の INSERT に `created_by='system'`, `updated_by='system'`, `version=1`, `attributes_schema_version=1` を追加
- `layer_schema_version` への初期行投入は migration 0106 が行うので、seed 側では不要（順序: 001_init → 002_seed → migration 群、と仮定）

### 含まない
- 大量シード（負荷確認用）
- 履歴シード

## 受け入れ条件 (Acceptance Criteria)
- [ ] `docker compose down -v && docker compose up -d` 後、`/api/layers` がエラーなく返る
- [ ] サンプル圃場の schema_json に fields が入っている
- [ ] feature_current の全行に created_by が入っている

## 影響ファイル
- `D:\proj\agri-gis\db\init\002_seed.sql` (変更)

## 実装ノート
- 既存形式を壊さないため、INSERT 列を明示的に列挙してから VALUES を書く
- `gen_random_uuid()` の依存は pgcrypto/postgis 同梱で問題なし

```sql
INSERT INTO layers (layer_name, layer_type, schema_json, schema_version)
VALUES
    ('サンプル圃場', 'polygon',
     '{"fields":[{"key":"name","type":"string","required":true,"label":"圃場名"},{"key":"crop","type":"string","required":false,"label":"作物"}]}'::jsonb, 1),
    ('サンプル観測点', 'point',
     '{"fields":[{"key":"name","type":"string","required":true,"label":"観測点名"}]}'::jsonb, 1);

INSERT INTO feature_current (
    layer_id, entity_id, geom, attributes,
    created_by, updated_by, version, attributes_schema_version
)
VALUES
    (1, gen_random_uuid(),
     ST_Transform(ST_GeomFromText('POLYGON((143.200 42.910, 143.205 42.910, 143.205 42.913, 143.200 42.913, 143.200 42.910))', 4326), 3857),
     '{"name":"A圃場","crop":"じゃがいも"}'::jsonb,
     'system', 'system', 1, 1),
    ...
```

注意点:
- migration を流す前 (init だけの状態) ではこの seed は古いスキーマで失敗する。順序「init → migration」で運用するか、`docker-entrypoint-initdb.d` には init のみ置く運用にしてマイグレーションは手動適用にする（0101 で決めたルールに従う）
- 本イシューでは「init → migration が順に流された結果として完成形」を目標にし、init/002_seed.sql は **マイグレーション適用後** のスキーマを前提に書く

## テスト観点
- 0301 系では Testcontainers で `init → migration` の順に流すので、その経路で動くことを確認
