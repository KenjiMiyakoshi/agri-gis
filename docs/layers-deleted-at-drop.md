# layers.deleted_at DROP (Phase E' E'1)

Phase E で残置した `layers.deleted_at` 列を完全削除し、`valid_to = '9999-12-31'::date` ベースの判定に統一する。

## 背景

Phase A `feature_current/feature_history` で確立した「半開区間 + append-only」イディオムを、Phase E で `layers/layer_history` に横展開した際、後方互換のため `deleted_at` を残し二重書きで動かしていた。

```
fn_layer_delete v2:
  UPDATE layers
     SET valid_to = CURRENT_DATE,
         deleted_at = now()   ← 二重書き
   WHERE layer_id = p_layer_id;
```

API 側の WHERE 条件も `AND l.deleted_at IS NULL` のまま、`valid_to` ベース判定への切替が未完了。

## 採用方針

**完全 DROP + 全 WHERE 条件を `valid_to = '9999-12-31'::date` に置換。**

### Migration `0E08_drop_layers_deleted_at.sql`

```sql
-- up
ALTER TABLE layers DROP COLUMN deleted_at;

-- down (best effort)
ALTER TABLE layers ADD COLUMN deleted_at TIMESTAMPTZ NULL;
UPDATE layers SET deleted_at = now()
 WHERE valid_to <> '9999-12-31'::date;
```

### 関数 v3 (3 本 CREATE OR REPLACE)

- `fn_layer_delete`: `UPDATE layers SET valid_to = CURRENT_DATE` のみ (deleted_at 操作なし)
- `fn_layer_update`: WHERE 条件から `deleted_at IS NULL` 削除、`valid_to = '9999-12-31'::date` で active 判定
- `fn_layer_style_upsert`: 同上

### API endpoint WHERE 置換 (4 ファイル / 18 SQL 箇所)

```diff
-AND l.deleted_at IS NULL
+AND l.valid_to = '9999-12-31'::date
```

対象:
- `AdminLayersEndpoints.cs` (GET / 3 SQL + LoadLayerAsync + 認可確認 = 6 箇所)
- `LayerEndpoints.cs` (GET / + extent + /at + schema = 4 箇所)
- `AdminLayerStyleEndpoints.cs` (GET style + PUT style = 2 箇所)
- `FeatureEndpoints.cs` (GET /{entityId} + history + POST + PATCH + DELETE + ?layerId 410 = 6 箇所)

### DTO 改修

- `api/Dto/AdminLayerDtos.cs`: `LayerAdminDto.DeletedAt` 列削除
- `windos-app/Dto/LayerAdminDto.cs`: 連鎖修正
- 履歴情報は `layer_history.archived_at` + `archived_reason` で代替

### テスト書換

`FeatureEndpointsDeletedAtRegressionTests`:
- 旧: `UPDATE layers SET deleted_at = now() WHERE layer_id = 1` で論理削除を模擬
- 新: `SELECT fn_layer_delete(1, 'alice', '...', user_id, org_id)` 呼び出しで論理削除
- 回帰意図 (削除済 layer の feature endpoint 弾き) は維持

## 受入条件

1. `\d layers` に `deleted_at` カラムなし
2. `SELECT fn_layer_delete(1, ...)` 実行後、`SELECT * FROM layers WHERE layer_id = 1` で `valid_to` が `CURRENT_DATE`
3. `GET /api/admin/layers` で削除済 layer が返らない
4. `GET /api/admin/layers?includeDeleted=true` で全 layer (削除済含む) が返る (この場合は `valid_to <> '9999-12-31'` フィルタ無し)
5. `api.tests` 全 green

## 関連

- `PHASE_E_PRIME_INDEX.md`
- Phase E E105 (`fn_layer_delete v2`) — 元の二重書き経路
- メモリ `bitemporal_audit.md`
