# 0109: `fn_feature_delete` 実装 (履歴退避)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | 0104, 0105 |
| Blocks | 0212 |

## 概要
フィーチャを論理削除する PL/pgSQL 関数 `fn_feature_delete` を実装する。`feature_current` から物理削除する前に `feature_history` に `archived_reason='delete'` で退避する。

## 背景・目的
案 B' は論理削除＝履歴退避モデル。削除後も history と audit_log から完全に追跡可能でなければならない。

## スコープ
### 含む
- `fn_feature_delete(p_entity_id UUID, p_actor TEXT, p_request_id TEXT) RETURNS VOID`
- 旧行を feature_history に `archived_reason='delete'` で退避
- feature_current から物理 DELETE
- audit_log に `action='feature_delete'`, `before_doc=<削除前>`, `after_doc=NULL`
- `db/migration/008_fn_feature_delete.sql`

### 含まない
- 削除取り消し（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 2 回実行してもエラーにならない
- [ ] `p_actor` 空で例外 (22023)
- [ ] 対象 entity が存在しない時 02000
- [ ] 成功後、`feature_current` に対象行が存在しない
- [ ] `feature_history` に `archived_reason='delete'` の行が 1 つ増える
- [ ] `audit_log` に 1 行追加

## 影響ファイル
- `D:\proj\agri-gis\db\migration\008_fn_feature_delete.sql` (新規)

## 実装ノート
```sql
-- 008_fn_feature_delete.sql
CREATE OR REPLACE FUNCTION fn_feature_delete(
    p_entity_id UUID,
    p_actor TEXT,
    p_request_id TEXT
) RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_cur feature_current%ROWTYPE;
    v_before JSONB;
BEGIN
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;

    SELECT * INTO v_cur FROM feature_current WHERE entity_id = p_entity_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'entity not found: %', p_entity_id USING ERRCODE = '02000';
    END IF;

    SELECT to_jsonb(v_cur) INTO v_before;

    INSERT INTO feature_history (
        feature_id, layer_id, entity_id, geom, attributes,
        attributes_schema_version, valid_from, valid_to,
        version, created_at, updated_at, created_by, updated_by,
        archived_at, archived_by, archived_reason
    ) VALUES (
        v_cur.feature_id, v_cur.layer_id, v_cur.entity_id, v_cur.geom, v_cur.attributes,
        v_cur.attributes_schema_version, v_cur.valid_from, v_cur.valid_to,
        v_cur.version, v_cur.created_at, v_cur.updated_at, v_cur.created_by, v_cur.updated_by,
        now(), p_actor, 'delete'
    );

    DELETE FROM feature_current WHERE entity_id = p_entity_id;

    INSERT INTO audit_log (actor, action, target_table, layer_id, entity_id, feature_id, before_doc, after_doc, request_id)
    VALUES (p_actor, 'feature_delete', 'feature_current', v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
            v_before, NULL, p_request_id);
END;
$$;
```

注意点:
- `archived_reason='delete'` で履歴をフィルタすれば削除された entity を一覧化できる
- `valid_to` は退避時点ではまだ '9999-12-31' のままで OK（asOf 検索は archived_reason を別途見る前提）

## テスト観点
- 0303: DELETE 後 current=0, history=+1 (archived_reason='delete'), audit=+1
- 0304: 存在しない entity の DELETE で 404
