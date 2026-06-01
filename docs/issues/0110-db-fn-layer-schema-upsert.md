# 0110: `fn_layer_schema_upsert` 実装

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0102, 0106 |
| Blocks | 0207 |

## 概要
レイヤの属性スキーマを更新し、同時に `layer_schema_version` に履歴を追記する PL/pgSQL 関数 `fn_layer_schema_upsert` を実装する。

## 背景・目的
案 B' は schema_json をオンラインで更新できる。更新するたびに `schema_version` をインクリメントし、`layer_schema_version` に append-only で履歴を残し、旧行の `valid_to` を埋める。

## スコープ
### 含む
- `fn_layer_schema_upsert(p_layer_id INT, p_schema_json JSONB, p_actor TEXT) RETURNS INT (new_schema_version)`
- 旧 `layer_schema_version` の最新行の `valid_to` を `now()` で埋める
- `layers.schema_json` を新値で更新、`schema_version` をインクリメント
- 新 `layer_schema_version` 行を append
- audit_log に `action='schema_upsert'`
- `db/migration/009_fn_layer_schema_upsert.sql`

### 含まない
- schema_json の構造バリデーション（API 層で実装）
- 既存フィーチャの再バリデーション（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 2 回実行してもエラーにならない
- [ ] `p_actor` 空で例外
- [ ] `p_layer_id` が存在しないと例外
- [ ] 成功後、`layers.schema_version` が +1
- [ ] `layer_schema_version` に新 schema_version の行が増え、旧行の `valid_to` が `now()` で埋まる
- [ ] audit_log に 1 行

## 影響ファイル
- `D:\proj\agri-gis\db\migration\009_fn_layer_schema_upsert.sql` (新規)

## 実装ノート
```sql
-- 009_fn_layer_schema_upsert.sql
CREATE OR REPLACE FUNCTION fn_layer_schema_upsert(
    p_layer_id INT,
    p_schema_json JSONB,
    p_actor TEXT
) RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    v_old_version INT;
    v_new_version INT;
    v_before JSONB;
    v_after JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT schema_version, to_jsonb(l.*) INTO v_old_version, v_before
    FROM layers l WHERE layer_id = p_layer_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found: %', p_layer_id USING ERRCODE = '02000';
    END IF;

    v_new_version := v_old_version + 1;

    -- 旧 layer_schema_version の最新行に valid_to を入れる
    UPDATE layer_schema_version
    SET valid_to = now()
    WHERE layer_id = p_layer_id AND schema_version = v_old_version AND valid_to IS NULL;

    -- 新行を追加
    INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
    VALUES (p_layer_id, v_new_version, p_schema_json, now(), NULL, p_actor);

    -- layers 本体を更新
    UPDATE layers
    SET schema_json = p_schema_json, schema_version = v_new_version
    WHERE layer_id = p_layer_id;

    SELECT to_jsonb(l.*) INTO v_after FROM layers l WHERE layer_id = p_layer_id;

    INSERT INTO audit_log (actor, action, target_table, layer_id, entity_id, feature_id, before_doc, after_doc, request_id)
    VALUES (p_actor, 'schema_upsert', 'layers', p_layer_id, NULL, NULL, v_before, v_after, NULL);

    RETURN v_new_version;
END;
$$;
```

注意点:
- request_id は schema_upsert では取らない実装（必要なら引数に追加）
- スキーマ DOWNGRADE は本関数では扱わない（常に +1）

## テスト観点
- スキーマ更新で schema_version が +1、layer_schema_version の旧行に valid_to が入る
- 連続更新で履歴が時系列で並ぶ
