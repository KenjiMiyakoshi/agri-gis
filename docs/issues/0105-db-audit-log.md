# 0105: `audit_log` テーブル新設

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0101 |
| Blocks | 0107, 0108, 0109, 0303 |

## 概要
すべての書き込み (insert/update/delete/schema_upsert) を記録する `audit_log` テーブルを新設する。

## 背景・目的
案 B' で「実装初日から監査ログを敷く」のが要件。リクエスト ID、actor、before/after JSONB を残すことで後追い検証ができるようにする。

## スコープ
### 含む
- `audit_log` テーブル
- インデックス (`(occurred_at DESC)`, `(target_table, entity_id)`)
- `db/migration/004_audit_log.sql`

### 含まない
- 書き込みロジック (0107, 0108, 0109, 0110 の関数で実装)
- 監査ログの参照 API（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `\d audit_log` で全カラムが揃っている
- [ ] `occurred_at` への DESC インデックスがある
- [ ] `target_table` + `entity_id` への複合インデックスがある
- [ ] 2 回実行してもエラーにならない

## 影響ファイル
- `D:\proj\agri-gis\db\migration\004_audit_log.sql` (新規)

## 実装ノート
```sql
-- 004_audit_log.sql
CREATE TABLE IF NOT EXISTS audit_log (
    audit_id BIGSERIAL PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    actor TEXT NOT NULL,
    action TEXT NOT NULL,                -- 'feature_insert' | 'feature_update' | 'feature_delete' | 'schema_upsert'
    target_table TEXT NOT NULL,          -- 'feature_current' | 'layers' など
    layer_id INTEGER,
    entity_id UUID,
    feature_id BIGINT,
    before_doc JSONB,
    after_doc JSONB,
    request_id TEXT
);

CREATE INDEX IF NOT EXISTS idx_audit_log_occurred
    ON audit_log (occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_audit_log_target
    ON audit_log (target_table, entity_id);
```

注意点:
- `before_doc` / `after_doc` は「行のスナップショット (JSONB)」。`row_to_json(t)::jsonb` のような形で関数内で作る
- `action` は文字列でゆるく持つ（CHECK 制約はあえて付けない、将来追加しやすく）

## テスト観点
- 0303 不変条件テストで「INSERT/UPDATE/DELETE のたびに audit_log が +1 行」を確認
- request_id が API 側で生成された UUID と一致する（0304）
