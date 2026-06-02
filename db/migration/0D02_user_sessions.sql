-- D102 (WD1): user_sessions テーブル新設
-- Phase D 採用案 §2.4 + D103: JWT 発行ごとに 1 行作成、logout で deleted_at 埋め
-- selection_sets (0D03) や将来の audit log 等から session_id FK で参照される
-- jwt_jti は JWT の jti claim を格納 (UNIQUE で二重発行を検知)

CREATE TABLE IF NOT EXISTS user_sessions (
    session_id  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID         NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    jwt_jti     TEXT         NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at  TIMESTAMPTZ  NULL
);

-- jti は active な間のみ UNIQUE。論理削除済の同 jti は再投入可能 (ただし JWT 仕様上は 1 度限り)
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_sessions_jti_alive
    ON user_sessions(jwt_jti) WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_user_sessions_user_id
    ON user_sessions(user_id);

-- active session の高速参照 (JwtBearer OnTokenValidated で IsActive 判定)
CREATE INDEX IF NOT EXISTS ix_user_sessions_active
    ON user_sessions(session_id) WHERE deleted_at IS NULL;

COMMENT ON TABLE user_sessions IS
    'Phase D D102: JWT lifecycle 管理。発行で INSERT、logout で deleted_at。selection_sets.session_id 親テーブル。';
COMMENT ON COLUMN user_sessions.jwt_jti IS
    'JWT jti claim の格納。active な間 UNIQUE。';
COMMENT ON COLUMN user_sessions.deleted_at IS
    'logout タイムスタンプ。NULL=active。CASCADE 削除のトリガーになる。';
