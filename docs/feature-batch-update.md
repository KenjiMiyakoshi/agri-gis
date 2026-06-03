# Feature Batch Update (Phase D' D'4)

`POST /api/features:batch` で N 件の属性を 1 トランザクションで更新する API。

## 背景

現状 `PATCH /api/features/{entityId}` 1 件単位。WinForms の `AttributeEditorControl` で 10 件選択して保存しても、10 回 HTTP が飛ぶ。WAN 越しでは遅延倍化、audit_log も 10 行に分散。

## 採用案: all-or-nothing バッチ

### リクエスト

```http
POST /api/features:batch
Content-Type: application/json
Authorization: Bearer ...

{
  "entityIds": [
    "550e8400-e29b-41d4-a716-446655440000",
    "550e8400-e29b-41d4-a716-446655440001",
    "550e8400-e29b-41d4-a716-446655440002"
  ],
  "ifMatchVersions": [3, 5, 2],
  "attributesPatch": {
    "fertilizer_type": "compost",
    "applied_at": "2026-06-01"
  }
}
```

- `entityIds` と `ifMatchVersions` は同じ順序・同じ長さ
- 全件同じ `attributesPatch` (RFC 7396 merge patch) を適用
- 全件同じ layer_id でなくても可 (異なる layer 跨ぎ、ただし layer 同士の整合性 = layer_schema の current version への compatibility は呼び出し側責任)

### レスポンス (成功)

```http
200 OK
{
  "results": [
    { "entityId": "550e8400-...0000", "newVersion": 4, "validFrom": "2026-06-03" },
    { "entityId": "550e8400-...0001", "newVersion": 6, "validFrom": "2026-06-03" },
    { "entityId": "550e8400-...0002", "newVersion": 3, "validFrom": "2026-06-03" }
  ],
  "count": 3
}
```

### レスポンス (1 件 version mismatch → 全件 rollback)

```http
409 Conflict
{
  "type": "https://docs.agri-gis/errors/batch-version-mismatch",
  "title": "Optimistic lock failed in batch update",
  "status": 409,
  "detail": "1 of 3 entities had stale version",
  "mismatchedEntityIds": [
    "550e8400-...0001"
  ],
  "expectedVersions": [5],
  "actualVersions": [7]
}
```

全件 rollback、DB は変更されず。

### レスポンス (entity not found)

```http
404 Not Found
{
  "type": "https://docs.agri-gis/errors/batch-entity-not-found",
  "title": "Some entities not found",
  "missingEntityIds": ["..."]
}
```

## 落選案

### partial success

1 件成功、1 件失敗で 207 Multi-Status:
- 半端状態が監査しにくい
- Phase A/E の atomic 路線と矛盾
- 「結局どこまで成功したか」を判定する追加 round-trip が必要

### 並列 PATCH 1 件 × N

クライアント側で `Promise.all([PATCH x10])`:
- HTTP ラウンドトリップは並列化で削減できる
- ただし**トランザクション一貫性が無い** (途中失敗で中途半端、audit_log も分散)
- DB connection の並列度依存 (Npgsql の pool ピンニング)

## DB 関数 (D'103)

```sql
-- 0F01_fn_feature_batch_update.sql
CREATE OR REPLACE FUNCTION fn_feature_batch_update(
    p_entity_ids        UUID[],
    p_if_match_versions INT[],
    p_attributes_patch  JSONB,
    p_actor             TEXT,
    p_request_id        TEXT,
    p_user_id           UUID,
    p_org_id            INT
)
RETURNS TABLE(entity_id UUID, new_version INT, valid_from DATE)
LANGUAGE plpgsql
AS $$
DECLARE
    v_idx INT;
    v_eid UUID;
    v_expected INT;
    v_actual INT;
    v_mismatch_ids UUID[] := ARRAY[]::UUID[];
    v_mismatch_actual INT[] := ARRAY[]::INT[];
BEGIN
    -- 1. 入力検証
    IF p_actor IS NULL OR length(trim(p_actor)) = 0 THEN
        RAISE EXCEPTION 'actor is required' USING ERRCODE = '22023';
    END IF;
    IF array_length(p_entity_ids, 1) IS NULL THEN
        RAISE EXCEPTION 'entity_ids cannot be empty' USING ERRCODE = '22023';
    END IF;
    IF array_length(p_entity_ids, 1) <> array_length(p_if_match_versions, 1) THEN
        RAISE EXCEPTION 'entity_ids and if_match_versions must have same length'
            USING ERRCODE = '22023';
    END IF;

    -- 2. 全件突合 (1 件でも mismatch → 全件失敗)
    FOR v_idx IN 1..array_length(p_entity_ids, 1) LOOP
        v_eid := p_entity_ids[v_idx];
        v_expected := p_if_match_versions[v_idx];
        SELECT version INTO v_actual
          FROM feature_current
         WHERE feature_current.entity_id = v_eid
           FOR UPDATE;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'entity not found: %', v_eid USING ERRCODE = '02000';
        END IF;
        IF v_actual <> v_expected THEN
            v_mismatch_ids := array_append(v_mismatch_ids, v_eid);
            v_mismatch_actual := array_append(v_mismatch_actual, v_actual);
        END IF;
    END LOOP;

    IF array_length(v_mismatch_ids, 1) > 0 THEN
        RAISE EXCEPTION 'optimistic_lock_failed: %', v_mismatch_ids
            USING ERRCODE = 'P0001',
                  HINT = format('mismatched_ids=%s, actual_versions=%s',
                                v_mismatch_ids, v_mismatch_actual);
    END IF;

    -- 3. 全件 update (Phase A fn_feature_update を内部 LOOP)
    FOR v_idx IN 1..array_length(p_entity_ids, 1) LOOP
        v_eid := p_entity_ids[v_idx];
        -- (既存 fn_feature_update の中身を inline、属性のみ patch)
        ...
        RETURN NEXT (v_eid, v_actual + 1, CURRENT_DATE);
    END LOOP;
END;
$$;
```

## API (D'104)

```csharp
group.MapPost("/:batch", async (
    FeatureBatchUpdateRequestDto req,
    ICurrentUser user,
    string requestId,
    NpgsqlDataSource db) =>
{
    if (req.EntityIds.Count != req.IfMatchVersions.Count)
        throw new ValidationException("entityIds and ifMatchVersions length mismatch");
    if (req.EntityIds.Count == 0)
        throw new ValidationException("entityIds cannot be empty");
    if (req.EntityIds.Count > 1000)
        throw new ValidationException("entityIds limit is 1000 per batch");

    await using var cmd = db.CreateCommand("SELECT * FROM fn_feature_batch_update(...)");
    cmd.Parameters.AddWithValue("eids", req.EntityIds.ToArray());
    cmd.Parameters.AddWithValue("vers", req.IfMatchVersions.ToArray());
    cmd.Parameters.AddWithValue("patch", req.AttributesPatch);
    cmd.Parameters.AddWithValue("actor", user.DisplayName);
    cmd.Parameters.AddWithValue("rid", requestId);
    cmd.Parameters.AddWithValue("uid", user.UserId);
    cmd.Parameters.AddWithValue("oid", user.OrgId);
    try {
        await using var r = await cmd.ExecuteReaderAsync();
        var results = new List<FeatureBatchUpdateResultDto>();
        while (await r.ReadAsync())
            results.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetDateOnly(2)));
        return Results.Ok(new FeatureBatchUpdateResponseDto(results, results.Count));
    } catch (PostgresException pe) when (pe.SqlState == "P0001"
                                          && pe.Message.Contains("optimistic_lock")) {
        // mismatched IDs を pe.Hint から parse して 409 返却
        return Results.Conflict(BuildConflictProblem(pe));
    }
})
.RequireAuthorization(p => p.RequireRole("admin", "general"));
```

## サイズ制限

- 1 リクエスト 1000 entity まで (DDoS 防止)
- それ以上は client 側でチャンク分割

## audit_log

各 entity に対して `fn_feature_update` と同じ audit_log 行が生成される (batch 全体で 1 行ではなく N 行)。`p_request_id` が全件同じになるので、後から「同 batch の N 件」を特定可能。

## WebGIS / WinForms 配線

### WebGIS (D'304)

```typescript
// webgis/src/api/client.ts
export async function postFeatureBatch(
  entityIds: string[],
  ifMatchVersions: number[],
  attributesPatch: object
): Promise<FeatureBatchUpdateResponse> {
  return await fetch('/api/features:batch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ entityIds, ifMatchVersions, attributesPatch })
  }).then(r => r.json());
}
```

### WinForms (D'304)

`AttributeEditorControl` を拡張:
- 単一選択 → 既存 PATCH 1 件版
- 複数選択 → 「N 件まとめて編集」モード → batch API 呼び出し
- 表示は全件共通の patch (差異がある場合は空文字 + 「変更しない」プレースホルダ)

## 受入条件

1. `POST /api/features:batch` で 10 件まとめ更新成功 (200 + 各 entity new version)
2. 1 件 version mismatch で 409 + 全件 rollback (DB に変更なし、audit_log にも追加なし)
3. entity not found で 404
4. 1000 件超で 400 (validation error)
5. audit_log に N 行追加、同 `request_id` で紐付け可能
6. WinForms 複数選択 → N 件まとめて編集 → 1 リクエストで完了 (Network 確認)

## テスト

- `FeatureBatchSuccessTests`: 10 件成功
- `FeatureBatchRollbackTests`: 1 件 mismatch → 全件 rollback、DB 不変
- `FeatureBatchAuthTests`: guest で 403
- `FeatureBatchSizeLimitTests`: 1001 件で 400
- `AttributeEditorBatchModeTests` (`windos-app.tests`): 複数選択時の挙動

## 関連

- `docs/PHASE_D_PRIME_INDEX.md`
- `docs/bitemporal-asof.md` (Phase A fn_feature_update イディオム)
- メモリ `bitemporal_audit.md`
