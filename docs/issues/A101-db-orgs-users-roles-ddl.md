# A101: organizations / users / user_roles DDL

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 1d |
| Depends on | なし |
| Blocks | A102, A106, A201, A207, A301, A302, A501 |

## 概要
認証基盤の核となる 3 テーブル (`organizations`, `users`, `user_roles`) を追加する。論理削除と部分 UNIQUE INDEX、role 値の CHECK 制約を含む。

## 背景・目的
採択案「案 P」の「ロールモデル: 多対多 `user_roles`、admin/general/guest の 3 固定」と「Admin CRUD: 論理削除 + 部分 UNIQUE INDEX」を実現する DDL レイヤ。後続の audit_log 拡張 (A102)、PL/pgSQL 引数拡張 (A106)、API の認証/認可 (A201〜)、Admin CRUD (A301/A302)、テスト seed (A501) のすべての前提となる。

## スコープ
### 含む
- `organizations` テーブル (id SERIAL PK, name, code UNIQUE, deleted_at, created_at, updated_at)
- `users` テーブル (user_id UUID PK, login_id, display_name, password_hash, org_id FK, deleted_at, created_at, updated_at)
- `user_roles` テーブル (user_id FK, role TEXT CHECK in ('admin','general','guest'), PK(user_id, role))
- 部分 UNIQUE INDEX: `organizations(code) WHERE deleted_at IS NULL`、`users(login_id) WHERE deleted_at IS NULL`
- `db/migration/0A01_auth_core_tables.sql`

### 含まない
- audit_log への `actor_user_id` 追加 (A102)
- 初期 admin の upsert ロジック (A207)
- seed データ投入 (A501)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `organizations.code` は `deleted_at IS NULL` のときのみ UNIQUE、論理削除済は同一 code を再利用可能
- [ ] `users.login_id` も同様に部分 UNIQUE
- [ ] `users.org_id` は `organizations.id` への FK、ON DELETE RESTRICT
- [ ] `user_roles.role` は CHECK 制約で `admin|general|guest` のみ許可
- [ ] `user_roles` の PK は `(user_id, role)` 複合主キー
- [ ] migration 適用後、`\d users` で確認可能
- [ ] 2 回適用してもエラーにならない（`IF NOT EXISTS` または migration 番号管理）

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A01_auth_core_tables.sql` (新規)

## 実装ノート
```sql
-- 0A01_auth_core_tables.sql
CREATE TABLE IF NOT EXISTS organizations (
    id          SERIAL PRIMARY KEY,
    name        TEXT NOT NULL,
    code        TEXT NOT NULL,
    deleted_at  TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_organizations_code_alive
    ON organizations(code) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS users (
    user_id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    login_id       TEXT NOT NULL,
    display_name   TEXT NOT NULL,
    password_hash  TEXT NOT NULL,
    org_id         INT NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    deleted_at     TIMESTAMPTZ,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_login_alive
    ON users(login_id) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS user_roles (
    user_id  UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    role     TEXT NOT NULL CHECK (role IN ('admin','general','guest')),
    PRIMARY KEY (user_id, role)
);
```

注意点:
- `gen_random_uuid()` は `pgcrypto` 拡張が必要（既存マイグレーションで有効化済か確認）
- migration 番号 `0A01` は Phase A 用に W シリーズと区別

## テスト観点
- A501 (SeedUsers) で alice/bob/carol が users + user_roles に挿入可能なこと
- A506 (AdminUsersCrudTests) で論理削除後の同一 login_id 再利用が成功
- A506 (AdminOrgsCrudTests) で論理削除後の同一 code 再利用が成功
- role CHECK 制約: 'viewer' を入れると 23514 (check_violation)
