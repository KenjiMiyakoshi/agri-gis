# 0108: `fn_feature_update` 実装 (楽観ロック)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 1d |
| Depends on | 0104, 0105, 0107 |
| Blocks | 0211 |

## 概要
フィーチャの属性 / 図形を更新する PL/pgSQL 関数 `fn_feature_update` を実装する。楽観ロック付き、旧行を feature_history に退避する。

## 背景・目的
案 B' の中核。更新は必ず history を 1 行積み、audit_log を 1 行積み、version をインクリメントする。`expected_version` 不一致は例外として 409 へマップ。

## スコープ
### 含む
- `fn_feature_update(p_entity_id UUID, p_new_geom_geojson_4326 TEXT, p_new_attributes JSONB, p_actor TEXT, p_expected_version INT, p_request_id TEXT, OUT new_version INT)`
- 楽観ロック: `expected_version != current.version` で例外 (`ERRCODE='40001'` シリアライゼーション失敗)
- geom は NULL なら変更しない（属性のみ更新可）、attributes も NULL なら変更しない
- 旧行を feature_history に `archived_reason='update'` で退避（archived_by = p_actor）
- audit_log に `action='feature_update'`, `before_doc`, `after_doc`
- `db/migration/007_fn_feature_update.sql`

### 含まない
- DELETE (0109)
- 属性スキーマ整合チェック（API 層で行う）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 2 回実行してもエラーにならない
- [ ] `p_actor` 空で例外 (22023)
- [ ] `expected_version` 不一致で例外 (40001)
- [ ] 成功時、`version = expected + 1`、`updated_by = p_actor`、`updated_at = now()`
- [ ] feature_history に旧行が `version=<更新前>`, `archived_reason='update'` で記録
- [ ] audit_log に before_doc / after_doc が記録
- [ ] `p_new_geom_geojson_4326 IS NULL` のときは geom は据え置き
- [ ] `p_new_attributes IS NULL` のときは attributes は据え置き

## 影響ファイル
- `D:\proj\agri-gis\db\migration\007_fn_feature_update.sql` (新規)

## 実装ノート
```sql
-- 007_fn_feature_update.sql
CREATE OR REPLACE FUNCTION fn_feature_update(
    p_entity_id UUID,
    p_new_geom_geojson_4326 TEXT,
    p_new_attributes JSONB,
    p_actor TEXT,
    p_expected_version INT,
    p_request_id TEXT,
    OUT new_version INT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_cur feature_current%ROWTYPE;
    v_before JSONB;
    v_after JSONB;
    v_new_geom geometry;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_cur FROM feature_current WHERE entity_id = p_entity_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'entity not found: %', p_entity_id USING ERRCODE = '02000';
    END IF;

    IF v_cur.version <> p_expected_version THEN
        RAISE EXCEPTION 'version mismatch: expected %, current %', p_expected_version, v_cur.version
            USING ERRCODE = '40001';
    END IF;

    SELECT to_jsonb(v_cur) INTO v_before;

    -- 旧行を history に退避
    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from, v_cur.valid_to,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'update'
    );

    -- current を更新
    IF p_new_geom_geojson_4326 IS NOT NULL THEN
        v_new_geom := ST_Transform(ST_SetSRID(ST_GeomFromGeoJSON(p_new_geom_geojson_4326), 4326), 3857);
    END IF;

    UPDATE feature_current SET
        geom = COALESCE(v_new_geom, geom),
        attributes = COALESCE(p_new_attributes, attributes),
        updated_at = now(),
        updated_by = p_actor,
        version = v_cur.version + 1
    WHERE entity_id = p_entity_id;

    new_version := v_cur.version + 1;

    SELECT to_jsonb(fc.*) INTO v_after
    FROM feature_current fc WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (actor, action, target_table, layer_id, entity_id, feature_id, before_doc, after_doc, request_id)
    VALUES (p_actor, 'feature_update', 'feature_current', v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
            v_before, v_after, p_request_id);
END;
$$;
```

注意点:
- `40001` (serialization_failure) を API 層で 409 にマップ（0204）
- `02000` (no_data) を 404 にマップ
- `22023` (invalid_parameter_value) を 400 にマップ

## テスト観点
- 0303: UPDATE 後 current=1 unchanged, history=+1, audit=+1, version=+1
- 0304: expected_version 不一致で 409
- 0304: geom のみ更新 / 属性のみ更新の両方
