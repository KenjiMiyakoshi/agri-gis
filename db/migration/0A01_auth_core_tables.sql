-- A101: 認証基盤の核 3 テーブル (organizations / users / user_roles)
-- 採択案「案 P」のロールモデル: 多対多 user_roles + admin/general/guest 3 固定値
-- 論理削除 (deleted_at) + 部分 UNIQUE INDEX で論理削除後の同一値再利用を許容

-- gen_random_uuid() は PG13+ で core 提供だが、念のため pgcrypto を有効化
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS organizations (
    id         SERIAL      PRIMARY KEY,
    name       TEXT        NOT NULL,
    code       TEXT        NOT NULL,
    deleted_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 論理削除済の同一 code は再利用可能
CREATE UNIQUE INDEX IF NOT EXISTS ux_organizations_code_alive
    ON organizations(code) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS users (
    user_id       UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    login_id      TEXT        NOT NULL,
    display_name  TEXT        NOT NULL,
    password_hash TEXT        NOT NULL,
    org_id        INTEGER     NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    deleted_at    TIMESTAMPTZ,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 論理削除済の同一 login_id は再利用可能
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_login_alive
    ON users(login_id) WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_users_org_id
    ON users(org_id);

CREATE TABLE IF NOT EXISTS user_roles (
    user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    role    TEXT NOT NULL CHECK (role IN ('admin', 'general', 'guest')),
    PRIMARY KEY (user_id, role)
);
