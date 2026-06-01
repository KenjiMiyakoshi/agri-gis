-- A102: audit_log に actor_user_id UUID 追加 + 旧 actor TEXT を display_name snapshot として温存
-- 採択案「案 P」の audit_log セクション準拠
-- 依存: A101 (users テーブル)
--
-- 注: 本マイグレーションでは actor_user_id を **NULL 許容** で追加する。
-- NOT NULL 化は A106 (PL/pgSQL 関数引数拡張) で API 側が user_id を渡すように
-- なってから別マイグレーションで適用する。順序：
--   A102 (本ファイル): 列追加 (nullable) + FK + INDEX
--   A106:              関数引数拡張で audit_log INSERT に actor_user_id を埋める
--   A106 末尾:         ALTER COLUMN ... SET NOT NULL

-- 1. NULL 許容で追加
ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS actor_user_id UUID;

-- 2. 既存データのバックフィル（開発環境：actor TEXT を login_id とみなして紐付け）
--    マッチしない行は actor_user_id NULL のまま残る（後で A106 が NOT NULL 化する前に DELETE）
UPDATE audit_log al
   SET actor_user_id = u.user_id
  FROM users u
 WHERE al.actor_user_id IS NULL
   AND al.actor = u.login_id;

-- 3. FK 制約を追加（既存なければ）
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
         WHERE constraint_name = 'fk_audit_log_user'
           AND table_name = 'audit_log'
    ) THEN
        ALTER TABLE audit_log
            ADD CONSTRAINT fk_audit_log_user
            FOREIGN KEY (actor_user_id) REFERENCES users(user_id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_audit_log_actor_user_id ON audit_log(actor_user_id);

-- 列の意味を変更：actor は display_name snapshot として扱う
COMMENT ON COLUMN audit_log.actor IS
    'display_name snapshot at audit time (kept for post-delete/rename tracking; rename to display_name in Phase B)';
COMMENT ON COLUMN audit_log.actor_user_id IS
    'FK to users.user_id; canonical actor identity (NOT NULL applied in A106 after API starts passing it)';
