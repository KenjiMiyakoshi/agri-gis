# A103: feature_current(entity_id) UNIQUE INDEX 追加 (H1 修復)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | なし |
| Blocks | A104, A106 |

## 概要
`feature_current(entity_id)` に UNIQUE INDEX を追加し、PL/pgSQL 関数群が暗黙に依存している「entity_id 一意」を物理的に強制する (H1 修復)。

## 背景・目的
採択案「案 P」の PL/pgSQL セクション:
> **H1 修復**: `feature_current(entity_id)` に UNIQUE INDEX

`fn_feature_update` / `fn_feature_delete` 等は `WHERE entity_id = p_entity_id` で 1 行を前提とするが、現状 entity_id に UNIQUE 制約が無く、論理的整合性が DB レベルで保証されていない。

## スコープ
### 含む
- `CREATE UNIQUE INDEX ux_feature_current_entity_id ON feature_current(entity_id)`
- `db/migration/0A03_feature_current_entity_unique.sql`

### 含まない
- entity_id 重複が既に存在する場合のクレンジング（開発段階で重複なし前提、もし出たら手で消す）

## 受け入れ条件 (Acceptance Criteria)
- [ ] UNIQUE INDEX `ux_feature_current_entity_id` が存在
- [ ] 同一 entity_id を 2 行 INSERT すると 23505 (unique_violation)
- [ ] migration を 2 回適用してもエラーにならない (`IF NOT EXISTS`)
- [ ] 既存 0303 系テストが green のまま

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A03_feature_current_entity_unique.sql` (新規)

## 実装ノート
```sql
-- 0A03_feature_current_entity_unique.sql
CREATE UNIQUE INDEX IF NOT EXISTS ux_feature_current_entity_id
    ON feature_current(entity_id);
```

注意点:
- もし既存データに重複があれば migration 失敗する。事前に `SELECT entity_id, count(*) FROM feature_current GROUP BY 1 HAVING count(*) > 1` で確認
- 0103 (feature_current 拡張) で entity_id 列追加済みである前提

## テスト観点
- 既存 feature_get / feature_update 系テストが grevn（H1 修復は後方互換）
- 新規回帰テストは特に作らず、A508 系で UNIQUE 違反の挙動を担保しなくて良い（DDL の制約は自明）
