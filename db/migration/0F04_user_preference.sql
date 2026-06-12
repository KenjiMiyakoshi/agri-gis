-- F'301: user_preference テーブル (Phase F' WF'3)
-- ユーザ単位の設定保持 (key/value JSONB)。Phase F' WF'3 では layer_order_v1 を初期用途とする。
--
-- 用途:
--   - layer_order_v1: WinForms CheckedListBox の表示 layer の順序 (上位 = 前面)
--   - 将来: theme 選好、画面レイアウト等を追加予定
--
-- 設計:
--   - PK (user_id, key) でユーザ × キーの一意性
--   - value JSONB で任意構造を保持
--   - updated_at で最終更新時刻記録 (TRIGGER 不要、API 側で now() を投入)
--   - 削除時は user CASCADE (users.deleted_at は論理削除のため物理削除は稀だが安全側)
--
-- バイテンポラル無し: 設定は現時点のみ評価、過去の選好を遡る要件は無い

CREATE TABLE IF NOT EXISTS user_preference (
    user_id    UUID        NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    key        TEXT        NOT NULL,
    value      JSONB       NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, key)
);

COMMENT ON TABLE user_preference IS
    'ユーザ単位の設定保持 (Phase F'' WF''3)。key/value JSONB で任意構造、現時点のみ評価。';
COMMENT ON COLUMN user_preference.key IS
    '設定キー (例: layer_order_v1)。アプリ側で命名規約を定める';
COMMENT ON COLUMN user_preference.value IS
    'JSONB 任意構造。例: layer_order_v1 → [5, 2, 1] (上位 = 前面)';
