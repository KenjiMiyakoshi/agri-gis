# 0107: `fn_feature_insert` 実装

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 1d |
| Depends on | 0103, 0105 |
| Blocks | 0108, 0210 |

## 概要
フィーチャを新規挿入する PL/pgSQL 関数 `fn_feature_insert` を実装する。

## 背景・目的
案 B' は書き込み経路を必ず関数経由に揃え、API は関数呼び出しのみで監査・整合性を担保する。INSERT 時に `attributes_schema_version` を layers から拾い、audit_log に 1 行残す。

## スコープ
### 含む
- `fn_feature_insert(p_layer_id INT, p_entity_id UUID, p_geom_geojson_4326 TEXT, p_attributes JSONB, p_actor TEXT, p_request_id TEXT) RETURNS BIGINT`
- GeoJSON (4326) を内部で `ST_GeomFromGeoJSON` → `ST_SetSRID` → `ST_Transform(.., 3857)` で格納
- layers.schema_version を読んで feature_current.attributes_schema_version にコピー
- audit_log に `action='feature_insert'`, `before_doc=NULL`, `after_doc=<挿入後の行>`
- `db/migration/006_fn_feature_insert.sql`

### 含まない
- 属性スキーマの構造バリデーション（必須欠落・型不一致）→ API 層 (0204) で実施。関数は受け取った JSONB をそのまま入れる
- UPDATE / DELETE (0108, 0109)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `CREATE OR REPLACE FUNCTION` で 2 回実行してもエラーにならない
- [ ] 戻り値が新しい `feature_id` (BIGINT)
- [ ] `p_actor` が空文字 / NULL なら例外 (`RAISE EXCEPTION USING ERRCODE='22023'`)
- [ ] 挿入後、`feature_current.created_by = p_actor`, `updated_by = p_actor`, `version = 1`
- [ ] `attributes_schema_version` が `layers.schema_version` と一致
- [ ] `audit_log` に 1 行追加され、`actor`, `request_id`, `after_doc` が埋まる

## 影響ファイル
- `D:\proj\agri-gis\db\migration\006_fn_feature_insert.sql` (新規)

## 実装ノート
```sql
-- 006_fn_feature_insert.sql
CREATE OR REPLACE FUNCTION fn_feature_insert(
    p_layer_id INT,
    p_entity_id UUID,
    p_geom_geojson_4326 TEXT,
    p_attributes JSONB,
    p_actor TEXT,
    p_request_id TEXT
) RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_feature_id BIGINT;
    v_schema_version INT;
    v_geom geometry;
    v_after_doc JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT schema_version INTO v_schema_version
    FROM layers WHERE layer_id = p_layer_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'layer not found: %', p_layer_id USING ERRCODE = '23503';
    END IF;

    v_geom := ST_Transform(ST_SetSRID(ST_GeomFromGeoJSON(p_geom_geojson_4326), 4326), 3857);

    INSERT INTO feature_current (
        layer_id, entity_id, geom, attributes,
        valid_from, valid_to,
        created_at, updated_at, created_by, updated_by,
        version, attributes_schema_version
    ) VALUES (
        p_layer_id, p_entity_id, v_geom, COALESCE(p_attributes, '{}'::jsonb),
        CURRENT_DATE, DATE '9999-12-31',
        now(), now(), p_actor, p_actor,
        1, v_schema_version
    )
    RETURNING feature_id INTO v_feature_id;

    SELECT to_jsonb(fc.*) INTO v_after_doc
    FROM feature_current fc WHERE feature_id = v_feature_id;

    INSERT INTO audit_log (actor, action, target_table, layer_id, entity_id, feature_id, before_doc, after_doc, request_id)
    VALUES (p_actor, 'feature_insert', 'feature_current', p_layer_id, p_entity_id, v_feature_id, NULL, v_after_doc, p_request_id);

    RETURN v_feature_id;
END;
$$;
```

注意点:
- `to_jsonb(fc.*)` は `geom` が geometry 型なのでバイナリで載る。read 側で扱いに困るなら `ST_AsGeoJSON` で書き換える。本イシューでは raw のままにする（監査は値が確認できれば OK）

## テスト観点
- 0303: INSERT 後 current=1, history=0, audit=+1
- 0304: X-Actor 空での 400 マッピング（API 側）
