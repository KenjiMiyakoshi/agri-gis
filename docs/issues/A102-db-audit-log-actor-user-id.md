# A102: audit_log に actor_user_id 追加 + actor を display_name 化

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | A101 |
| Blocks | A106, A507 |

## 概要
`audit_log` テーブルに `actor_user_id UUID NOT NULL` を追加し、既存 `actor TEXT` 列の意味を「display_name スナップショット」に変更する。

## 背景・目的
採択案「案 P」の audit_log セクション:
> `actor_user_id UUID NOT NULL` 追加 / 旧 `actor TEXT` 列は **`display_name` スナップショット**として温存（user 削除/rename 後も追跡可）

本イシューでは列追加のみで、`actor TEXT` のリネーム (display_name 列化) は Phase B に申し送り（採択案末尾）。意味だけ変える。

## スコープ
### 含む
- `audit_log.actor_user_id UUID NOT NULL` 追加 + FK to `users(user_id)` ON DELETE RESTRICT
- 既存データのバックフィル戦略（Phase A 開始時点で audit_log は空またはテスト用のみ前提なので TRUNCATE 可、または NULL を一時的に許容してバックフィル後 NOT NULL 化）
- INDEX `audit_log(actor_user_id)`
- `db/migration/0A02_audit_log_actor_user_id.sql`

### 含まない
- PL/pgSQL 関数の引数拡張 (A106 で `p_user_id` を渡すように)
- `actor TEXT` の列名 rename (Phase B)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `audit_log.actor_user_id` は NOT NULL、FK で users(user_id) を参照
- [ ] INDEX `ix_audit_log_actor_user_id` が存在
- [ ] 既存 `actor TEXT` 列は残置（display_name snapshot として扱う）
- [ ] migration 適用後、既存 audit_log 行がある場合はバックフィルされている、または開発環境では空でも可
- [ ] 2 回適用してもエラーにならない

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A02_audit_log_actor_user_id.sql` (新規)

## 実装ノート
```sql
-- 0A02_audit_log_actor_user_id.sql

-- Phase A 開始時点で実運用 audit_log は空前提。一時的に NULL 許容で追加し、
-- バックフィル後に NOT NULL 化する 2 段階を 1 マイグレーションでまとめる。
ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS actor_user_id UUID;

-- 既存データがあれば、actor TEXT を login_id とみなしてバックフィル（開発環境）
UPDATE audit_log al
SET actor_user_id = u.user_id
FROM users u
WHERE al.actor_user_id IS NULL
  AND al.actor = u.login_id;

-- 残った NULL は seed admin にフォールバック、もしくは削除（開発環境）
-- 本番運用前のテーブルなので DELETE WHERE actor_user_id IS NULL でもよい
DELETE FROM audit_log WHERE actor_user_id IS NULL;

ALTER TABLE audit_log
    ALTER COLUMN actor_user_id SET NOT NULL;

ALTER TABLE audit_log
    ADD CONSTRAINT fk_audit_log_user
    FOREIGN KEY (actor_user_id) REFERENCES users(user_id) ON DELETE RESTRICT;

CREATE INDEX IF NOT EXISTS ix_audit_log_actor_user_id ON audit_log(actor_user_id);

COMMENT ON COLUMN audit_log.actor    IS 'display_name snapshot at audit time (kept for post-delete/rename tracking; rename to display_name in Phase B)';
COMMENT ON COLUMN audit_log.actor_user_id IS 'FK to users.user_id; canonical actor identity';
```

注意点:
- 既存 `0105-db-audit-log.md` で作成された audit_log との整合性確認
- Phase B の申し送り: `actor` → `display_name` 列名変更 + `audit_log.user_displayname` 等への rename

## テスト観点
- A507 (AuditUserIdTests): INSERT/UPDATE/DELETE 系 API 後、audit_log.actor_user_id が呼び出しユーザの user_id と一致
- A507: audit_log.actor (display_name snapshot) が users.display_name と一致（rename 後も追跡可を回帰確認するのは Phase B 範囲）
