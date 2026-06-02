-- D102 (WD1): layers.style_json JSONB を追加
-- Phase D 採用案 §2.3: SLD ベースの theme 定義を DB で保管 (`PHASE_D_DESIGN_P.md` 採用)
-- 既存 layers レコードは '{}'::jsonb で埋まる (デフォルト)
-- 初期 default SLD は API 側で WD2 D203 (admin theme CRUD) 起動時に流し込む

ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS style_json JSONB NOT NULL DEFAULT '{}'::jsonb;

COMMENT ON COLUMN layers.style_json IS
    'Phase D D102: SLD-derived theme definitions (JSON shape: { themes: {name: {...}} }). WD2 で API 経由更新。';
