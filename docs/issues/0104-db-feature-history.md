# 0104: `feature_history` テーブル新設

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0103 |
| Blocks | 0108, 0109, 0208, 0209 |

## 概要
旧バージョンのフィーチャを退避するための `feature_history` テーブルを新設する。

## 背景・目的
案 B' は更新 / 削除のたびに旧行を `feature_history` に積む方式。asOf クエリと監査の両方を支える土台。`current` と完全に分けることで、`current` の検索性を犠牲にしない。

## スコープ
### 含む
- `feature_history` テーブル
- 必要なインデックス (`(entity_id, valid_to DESC)`, `(layer_id)`, GIST(geom))
- `db/migration/003_feature_history.sql`

### 含まない
- 退避ロジック (0108, 0109 の関数で実装)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `\d feature_history` で全カラムが揃っている
- [ ] `entity_id`, `valid_to` への複合インデックスがある
- [ ] `geom` への GIST インデックスがある
- [ ] 2 回実行してもエラーにならない

## 影響ファイル
- `D:\proj\agri-gis\db\migration\003_feature_history.sql` (新規)

## 実装ノート
```sql
-- 003_feature_history.sql
CREATE TABLE IF NOT EXISTS feature_history (
    history_id BIGSERIAL PRIMARY KEY,
    feature_id BIGINT NOT NULL,
    layer_id INTEGER NOT NULL REFERENCES layers(layer_id),
    entity_id UUID NOT NULL,
    geom geometry(Geometry, 3857),
    attributes JSONB NOT NULL DEFAULT '{}'::jsonb,
    attributes_schema_version INT NOT NULL,
    valid_from DATE NOT NULL,
    valid_to DATE NOT NULL,
    version INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    created_by TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    archived_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    archived_by TEXT NOT NULL,
    archived_reason TEXT NOT NULL CHECK (archived_reason IN ('update', 'delete'))
);

CREATE INDEX IF NOT EXISTS idx_feature_history_entity
    ON feature_history (entity_id, valid_to DESC);

CREATE INDEX IF NOT EXISTS idx_feature_history_layer
    ON feature_history (layer_id);

CREATE INDEX IF NOT EXISTS idx_feature_history_geom
    ON feature_history USING GIST (geom);
```

注意点:
- `history_id` は履歴行の主キー。`feature_id` は旧 `feature_current.feature_id` の写し（同じ値でも OK、新しい version で current 側が別 feature_id を取るならそれでも OK）
- `version` は退避時点の version（つまり current 側の更新前 version）

## テスト観点
- 0303 不変条件テストで「UPDATE 後 history が +1 行、version が古い値」を確認
