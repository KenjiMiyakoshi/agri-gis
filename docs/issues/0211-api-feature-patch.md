# 0211: `PATCH /api/features/{entityId}` 実装 (If-Match)

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 1d |
| Depends on | 0204, 0108 |
| Blocks | 0602 |

## 概要
属性および / または図形を更新する `PATCH /api/features/{entityId}` を実装する。`If-Match: <version>` で楽観ロック。

## 背景・目的
案 B' の楽観ロックを HTTP 標準に近い形で実現する。`If-Match` ヘッダで `expected_version` を渡し、不一致は 409。

## スコープ
### 含む
- `PATCH /api/features/{entityId:guid}`
- リクエストヘッダ: `If-Match: <int>` (**必須**, 無ければ 428 Precondition Required)
- リクエストボディ: `UpdateFeatureRequestDto { geometry?, attributes? }`
- 属性が来ているなら schema バリデーション（type/required）。null なら未指定扱いで据え置き
- `fn_feature_update` 呼び出し、`new_version` を取り出す
- レスポンス: 200 + `{ entityId, version }`
- 関数の SqlState を 0204 経由で 404/409/400 にマップ
- X-Actor 必須

### 含まない
- 部分属性更新で他属性を保持するか / 上書きするか
  - 採択: `attributes` が来たら完全置換（記事的な使い方になるが、案 B' は明示しない）
- batch update

## 受け入れ条件 (Acceptance Criteria)
- [ ] If-Match なしで 428
- [ ] If-Match の値が現在 version と不一致で 409
- [ ] 成功で 200 + `{ entityId, version: <new> }`
- [ ] 属性のみ更新（geometry なし）で動作
- [ ] geometry のみ更新（attributes なし）で動作
- [ ] 属性 schema 違反で 422
- [ ] X-Actor 無しで 400

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (追加)

## 実装ノート
```csharp
group.MapPatch("/{entityId:guid}",
    async (Guid entityId, UpdateFeatureRequestDto req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var actor = RequestContext.RequireActor(ctx);
    var rid   = RequestContext.GetRequestId(ctx);

    var ifMatch = ctx.Request.Headers["If-Match"].ToString();
    if (!int.TryParse(ifMatch, out var expected))
        return Results.StatusCode(428); // Precondition Required

    // 現行 layer_id / schema を取得
    int layerId, schemaVersion;
    LayerSchemaDto schema;
    await using (var c = db.CreateCommand(
        @"SELECT fc.layer_id, l.schema_version, l.schema_json
            FROM feature_current fc JOIN layers l ON l.layer_id = fc.layer_id
           WHERE fc.entity_id = @e"))
    {
        c.Parameters.AddWithValue("e", entityId);
        await using var rr = await c.ExecuteReaderAsync();
        if (!await rr.ReadAsync()) throw new NotFoundException($"entity {entityId}");
        layerId = rr.GetInt32(0);
        schemaVersion = rr.GetInt32(1);
        schema = JsonSerializer.Deserialize<LayerSchemaDto>(rr.GetString(2), JsonOpts)!;
    }

    if (req.Attributes is { } attrs)
    {
        var errs = AttributeValidator.Validate(schema, attrs);
        if (errs.Count > 0) throw new ValidationException(errs);
    }

    await using var cmd = db.CreateCommand(
        @"SELECT fn_feature_update(@e, @g, @a::jsonb, @act, @ev, @rid)");
    cmd.Parameters.AddWithValue("e", entityId);
    cmd.Parameters.AddWithValue("g", req.Geometry?.GetRawText() ?? (object)DBNull.Value);
    cmd.Parameters.AddWithValue("a",
        req.Attributes is null ? (object)DBNull.Value : JsonSerializer.Serialize(req.Attributes, JsonOpts));
    cmd.Parameters.AddWithValue("act", actor);
    cmd.Parameters.AddWithValue("ev", expected);
    cmd.Parameters.AddWithValue("rid", rid);

    var newVersion = (int)(await cmd.ExecuteScalarAsync())!;
    return Results.Ok(new { entityId, version = newVersion });
});
```

注意点:
- 428 Precondition Required を採用するか、空 If-Match を 400 にするかは案 B' の文面では曖昧。本イシューでは **428** を採用する旨を README に書く
- PostgresException(SqlState=40001) は 0204 で 409 にマップ済み

## テスト観点
- 0303: UPDATE 後 current=1 unchanged 数, history=+1, audit=+1, version=+1
- 0304: 楽観ロック不一致で 409
- 0304: attributes だけ / geometry だけのどちらも成功
