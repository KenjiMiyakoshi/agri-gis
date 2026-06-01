# A106: PL/pgSQL 関数引数の非破壊拡張 (p_user_id, p_org_id)

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 1d |
| Depends on | A101, A102, A104, A105 |
| Blocks | A201, A207 |

## 概要
全 PL/pgSQL feature/schema 操作関数の末尾に `p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL` を追加し、audit_log.actor_user_id を埋める。デフォルト NULL で既存呼び出し互換。

## 背景・目的
採択案「案 P」の PL/pgSQL セクション:
> **非破壊拡張**: `p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL` を末尾に追加

audit_log.actor_user_id (A102) を埋めるためには、PL/pgSQL から user_id を受け取れる必要がある。API 層 (A201) は ICurrentUser 経由でこれを渡す。

## スコープ
### 含む
- `fn_feature_insert(..., p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL)` 追加
- `fn_feature_update(..., p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL)` 追加
- `fn_feature_delete(..., p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL)` 追加
- `fn_layer_schema_upsert(..., p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL)` 追加
- 関数本体: audit_log INSERT 時 `actor_user_id = COALESCE(p_user_id, fn_resolve_user_id(p_actor))` あるいは厳格に `p_user_id` 必須運用（NULL なら例外）
- `db/migration/0A06_fn_args_extension.sql`

### 含まない
- 旧呼び出しシグネチャ削除（互換性のため残す。Phase B で削除）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 4 関数の引数末尾に `p_user_id UUID DEFAULT NULL, p_org_id INT DEFAULT NULL` が追加
- [ ] `p_user_id` 非 NULL の場合、audit_log.actor_user_id にそのまま入る
- [ ] `p_user_id` NULL の場合、`p_actor` を login_id とみなして users から逆引きする（Phase A の互換策、Phase B で NOT NULL 強制）
- [ ] 逆引きも失敗したら例外 22023
- [ ] 既存呼び出し (PHASE-A 移行前のテスト) が引き続き動く（DEFAULT NULL のおかげで）

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A06_fn_args_extension.sql` (新規)
- A104/A105 で再定義された関数を CREATE OR REPLACE で上書き

## 実装ノート
```sql
-- 0A06_fn_args_extension.sql

CREATE OR REPLACE FUNCTION fn_feature_update(
    p_entity_id UUID,
    p_new_geom_geojson_4326 TEXT,
    p_new_attributes JSONB,
    p_actor TEXT,
    p_expected_version INT,
    p_request_id TEXT,
    p_user_id UUID DEFAULT NULL,     -- ★ 追加
    p_org_id  INT  DEFAULT NULL,     -- ★ 追加
    OUT new_version INT
)
LANGUAGE plpgsql AS $$
DECLARE
    v_user_id UUID;
BEGIN
    -- ... 既存ロジック ...

    v_user_id := COALESCE(
        p_user_id,
        (SELECT user_id FROM users WHERE login_id = p_actor AND deleted_at IS NULL)
    );
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'cannot resolve user_id for actor %', p_actor USING ERRCODE = '22023';
    END IF;

    INSERT INTO audit_log (
        actor, actor_user_id, action, target_table, layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, v_user_id, 'feature_update', 'feature_current',
        v_cur.layer_id, v_cur.entity_id, v_cur.feature_id,
        v_before, v_after, p_request_id
    );
END;
$$;
```

注意点:
- A105 で改修した geom strip ロジックを保ったまま引数追加する
- `p_actor` は display_name (snapshot) として audit_log.actor に入る
- `p_org_id` は Phase A では使わない（Phase B のテナント分離向け予約）

## テスト観点
- A507 (AuditUserIdTests): API 経由の操作で audit_log.actor_user_id が呼び出しユーザに一致
- 既存 0303/0304 テストが green（A501 で seed された users から逆引きされる）
