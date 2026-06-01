# A104: C1 修復 - fn_feature_update の valid_from/valid_to を CURRENT_DATE で接合

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | A103 |
| Blocks | A508 |

## 概要
`fn_feature_update` (および `fn_feature_delete` の history INSERT) において、`valid_to = CURRENT_DATE` で旧行を閉じ、新 current の `valid_from = CURRENT_DATE` で開く半開区間接合を実装する (C1 修復)。

## 背景・目的
採択案「案 P」の PL/pgSQL セクション:
> **C1 修復**: history INSERT 時 `valid_to = CURRENT_DATE`、current `valid_from = CURRENT_DATE`
> 同日 UPDATE 連発時のゼロ幅区間は仕様で許容（asOf は半開区間で空配列を返す）

これにより `AsOfTests.cs:53-61` の手動 UPDATE 補正が不要になる (A503 で削除)。

## スコープ
### 含む
- `fn_feature_update` の改修: history INSERT 時 `valid_to = CURRENT_DATE`、`UPDATE feature_current SET valid_from = CURRENT_DATE, ...`
- `fn_feature_delete` の改修: history INSERT 時 `valid_to = CURRENT_DATE`
- 仕様コメント追加（同日 UPDATE 連発でゼロ幅区間が history に積まれることを許容、asOf は半開区間 `[valid_from, valid_to)` で空配列）
- `db/migration/0A04_fn_feature_update_c1.sql`

### 含まない
- C2 修復 (geom strip) — A105
- PL/pgSQL 関数引数拡張 (p_user_id, p_org_id) — A106

## 受け入れ条件 (Acceptance Criteria)
- [ ] `fn_feature_update` 適用後、history.valid_to = CURRENT_DATE、新 current.valid_from = CURRENT_DATE
- [ ] 同日 2 回 UPDATE → history に 2 行、各 valid_to=CURRENT_DATE、ゼロ幅区間が存在しても OK
- [ ] `fn_feature_delete` も同様に history.valid_to = CURRENT_DATE
- [ ] 既存 0303/0304 テストが green（手動 UPDATE 補正なしで）
- [ ] AsOf 半開区間検索 `[valid_from, valid_to)` で CURRENT_DATE 当日 update 後の asOf=CURRENT_DATE は新 current を返す（旧 history は valid_to=今日 で含まない）

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A04_fn_feature_update_c1.sql` (新規)
- `D:\proj\agri-gis\db\migration\007_fn_feature_update.sql` の置き換え用ファイルとして 0A04 を後勝ち

## 実装ノート
既存 0108 の `fn_feature_update` をコピーし、以下 2 点を変更:

```sql
-- history INSERT 部
INSERT INTO feature_history (
    ..., valid_from, valid_to, ...,
    archived_at, archived_by, archived_reason
) VALUES (
    ..., v_cur.valid_from, CURRENT_DATE, ...,   -- ★ valid_to を CURRENT_DATE で閉じる
    now(), p_actor, 'update'
);

-- current UPDATE 部
UPDATE feature_current SET
    geom = COALESCE(v_new_geom, geom),
    attributes = COALESCE(p_new_attributes, attributes),
    valid_from = CURRENT_DATE,                  -- ★ 新 current は CURRENT_DATE で開く
    updated_at = now(),
    updated_by = p_actor,
    version = v_cur.version + 1
WHERE entity_id = p_entity_id;
```

`fn_feature_delete` も同様に history INSERT 時 `valid_to = CURRENT_DATE`。

注意点:
- 同日 UPDATE 連発時のゼロ幅区間 `[CURRENT_DATE, CURRENT_DATE)` は history に積まれるが、asOf 検索は半開区間なので 0 件ヒット → 仕様
- 既存 `AsOfTests.cs:53-61` の手動 `UPDATE feature_history SET valid_to = ...` は A503 で削除

## テスト観点
- A508 (C1RegressionTests): 当日 INSERT → 当日 UPDATE → asOf=今日 で新 current のみ返却 / asOf=過去日 で history を返却
- 既存 AsOf テストが手動補正なしで green
