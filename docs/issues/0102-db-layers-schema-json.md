# 0102: `layers` テーブル拡張 (schema_json, schema_version)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0101 |
| Blocks | 0106, 0110, 0111, 0205 |

## 概要
`layers` テーブルにレイヤごとの属性スキーマ (`schema_json`) とバージョン番号 (`schema_version`) を持たせる。

## 背景・目的
案 B' では属性スキーマはレイヤ単位で持ち、フィーチャ書き込み時にバリデーションする。`schema_json` を JSONB で持ち、`schema_version` を INT で持つことで、フィーチャ側の `attributes_schema_version` と紐付けてバイテンポラル管理する基盤を作る。

## スコープ
### 含む
- `layers.schema_json JSONB NOT NULL DEFAULT '{"fields":[]}'::jsonb`
- `layers.schema_version INT NOT NULL DEFAULT 1`
- 冪等マイグレーション SQL (`db/migration/001_layers_add_schema.sql`)

### 含まない
- `layer_schema_version` テーブル (0106)
- `fn_layer_schema_upsert` (0110)
- 既存シードの schema 充実 (0111)

## 受け入れ条件 (Acceptance Criteria)
- [ ] マイグレーション適用後、`\d layers` で `schema_json jsonb` と `schema_version integer` が存在
- [ ] 既存行 (`サンプル圃場`, `サンプル観測点`) の `schema_json` が `{"fields":[]}`、`schema_version` が 1 になっている
- [ ] 2 回実行してもエラーにならない

## 影響ファイル
- `D:\proj\agri-gis\db\migration\001_layers_add_schema.sql` (新規)

## 実装ノート
```sql
-- 001_layers_add_schema.sql
ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS schema_json JSONB NOT NULL DEFAULT '{"fields":[]}'::jsonb;

ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS schema_version INT NOT NULL DEFAULT 1;
```

`schema_json` の構造（参考、本イシューでは中身のバリデーションはしない）:
```json
{
  "fields": [
    { "key": "name", "type": "string", "required": true, "label": "圃場名" },
    { "key": "crop", "type": "string", "required": false, "label": "作物" }
  ]
}
```

## テスト観点
- 後続の 0301 系テストで「マイグレーション後に layers から `schema_json` が SELECT できる」を間接的に確認
