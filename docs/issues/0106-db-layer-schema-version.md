# 0106: `layer_schema_version` テーブル新設 + 初日稼働シード

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0102 |
| Blocks | 0110, 0111, 0207 |

## 概要
レイヤのスキーマ履歴を append-only で保持する `layer_schema_version` テーブルを新設し、既存レイヤぶんの初期行を投入する。

## 背景・目的
案 B' の重要要件「実装初日から稼働、空運用禁止」。スキーマを変更しても旧スキーマで書かれたフィーチャの属性整合性が後追いで検証できるよう、バージョンごとの schema_json を凍結保存する。

## スコープ
### 含む
- `layer_schema_version` テーブル
- 既存レイヤ (layer_id=1, 2) ぶんの初期行を投入（schema_version=1, schema_json=`{"fields":[]}`, valid_from=now()）
- `db/migration/005_layer_schema_version.sql`

### 含まない
- `fn_layer_schema_upsert` 関数 (0110)
- 実際の有用な schema_json 内容（0111 で投入）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `\d layer_schema_version` で全カラムが揃っている
- [ ] (layer_id, schema_version) のユニーク制約がある
- [ ] 既存 layer_id=1, 2 にそれぞれ schema_version=1 の行が入っている
- [ ] 2 回実行してもエラーにならない（`ON CONFLICT DO NOTHING`）

## 影響ファイル
- `D:\proj\agri-gis\db\migration\005_layer_schema_version.sql` (新規)

## 実装ノート
```sql
-- 005_layer_schema_version.sql
CREATE TABLE IF NOT EXISTS layer_schema_version (
    layer_id INTEGER NOT NULL REFERENCES layers(layer_id),
    schema_version INT NOT NULL,
    schema_json JSONB NOT NULL,
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ,
    created_by TEXT NOT NULL,
    PRIMARY KEY (layer_id, schema_version)
);

CREATE INDEX IF NOT EXISTS idx_layer_schema_version_layer
    ON layer_schema_version (layer_id, valid_from DESC);

-- 既存レイヤの初期行を投入（冪等）
INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
SELECT layer_id, schema_version, schema_json, now(), NULL, 'system'
FROM layers
ON CONFLICT (layer_id, schema_version) DO NOTHING;
```

注意点:
- `valid_to IS NULL` が現行スキーマを表す
- 新バージョンを追加する際は古い行の `valid_to` を埋めて新行を追加する（0110 で実装）
- `created_by` は seed 投入時は 'system'

## テスト観点
- 0303 系で「schema 更新時に layer_schema_version が +1 行され、旧行の valid_to が埋まる」を 0304 でも確認
