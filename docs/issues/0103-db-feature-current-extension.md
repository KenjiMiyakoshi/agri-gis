# 0103: `feature_current` テーブル拡張 (created_by/updated_by/version/schema_version, TIMESTAMPTZ化)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0101 |
| Blocks | 0104, 0107, 0108, 0208 |

## 概要
`feature_current` にユーザ追跡カラム・楽観ロック用 `version`・スキーマ整合性用 `attributes_schema_version` を追加し、`created_at` / `updated_at` を `TIMESTAMPTZ` に変更する。

## 背景・目的
案 B' で必須の監査・楽観ロック・スキーマ追跡を `feature_current` に組み込む。タイムゾーン混在を解消するため `TIMESTAMP` → `TIMESTAMPTZ` に揃える（`valid_from` / `valid_to` は asOf が DATE 粒度なので DATE のまま据え置き）。

## スコープ
### 含む
- `feature_current.created_by TEXT NOT NULL`
- `feature_current.updated_by TEXT NOT NULL`
- `feature_current.version INT NOT NULL DEFAULT 1`
- `feature_current.attributes_schema_version INT NOT NULL`
- `feature_current.created_at TIMESTAMPTZ` 変換
- `feature_current.updated_at TIMESTAMPTZ` 変換
- `db/migration/002_feature_current_audit.sql`

### 含まない
- `feature_history` (0104)
- 書き込み関数 (0107, 0108)
- 既存データ移行用シード追加 (0111)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `\d feature_current` で `created_by`, `updated_by`, `version`, `attributes_schema_version` が NOT NULL で存在
- [ ] `created_at`, `updated_at` が `timestamp with time zone`
- [ ] `valid_from`, `valid_to` は `date` のまま
- [ ] 既存行が NULL 違反を起こさない（DEFAULT または UPDATE で値を入れる）
- [ ] 2 回実行してもエラーにならない

## 影響ファイル
- `D:\proj\agri-gis\db\migration\002_feature_current_audit.sql` (新規)

## 実装ノート
```sql
-- 002_feature_current_audit.sql
-- 既存行を埋めるために一度 DEFAULT 付きで追加し、後でデフォルトを外す方針もあるが、
-- 開発初期なのでシンプルに NOT NULL DEFAULT で追加する。

ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS created_by TEXT NOT NULL DEFAULT 'system';
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS updated_by TEXT NOT NULL DEFAULT 'system';
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS version INT NOT NULL DEFAULT 1;
ALTER TABLE feature_current
    ADD COLUMN IF NOT EXISTS attributes_schema_version INT NOT NULL DEFAULT 1;

-- TIMESTAMP → TIMESTAMPTZ
ALTER TABLE feature_current
    ALTER COLUMN created_at TYPE TIMESTAMPTZ USING created_at AT TIME ZONE 'Asia/Tokyo';
ALTER TABLE feature_current
    ALTER COLUMN updated_at TYPE TIMESTAMPTZ USING updated_at AT TIME ZONE 'Asia/Tokyo';

-- DEFAULT を維持（新規行は now() のままにしたいので default を再設定）
ALTER TABLE feature_current
    ALTER COLUMN created_at SET DEFAULT now();
ALTER TABLE feature_current
    ALTER COLUMN updated_at SET DEFAULT now();
```

注意点:
- `DEFAULT 'system'` は既存行を埋めるためだけのもの。アプリ側からは関数経由で必ず `actor` を渡す
- (entity_id, valid_from) のユニーク制約は今は付けない（後続イシューで必要なら追加）

## テスト観点
- 後続 0303 テストで「INSERT 後 `created_by` が X-Actor の値になっている」を確認
