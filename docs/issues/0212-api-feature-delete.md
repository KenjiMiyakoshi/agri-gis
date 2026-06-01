# 0212: `DELETE /api/features/{entityId}` 実装

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0204, 0109 |
| Blocks | なし |

## 概要
論理削除（履歴退避）として動く `DELETE /api/features/{entityId}` を実装する。

## 背景・目的
案 B' の論理削除モデル。current から消えるが history と audit_log には完全な痕跡が残る。

## スコープ
### 含む
- `DELETE /api/features/{entityId:guid}`
- X-Actor 必須
- `fn_feature_delete` 呼び出し
- 成功で 204 No Content
- 存在しない entity で 404

### 含まない
- バルク削除
- ハードデリート (本サイクル外)

## 受け入れ条件 (Acceptance Criteria)
- [ ] 成功で 204
- [ ] X-Actor 無しで 400
- [ ] 存在しない entityId で 404
- [ ] DELETE 後、`GET /api/features/{entityId}` が 404 になる
- [ ] DELETE 後、`GET /api/features/{entityId}/history` が `archived_reason='delete'` を含む

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (追加)

## 実装ノート
```csharp
group.MapDelete("/{entityId:guid}",
    async (Guid entityId, HttpContext ctx, NpgsqlDataSource db) =>
{
    var actor = RequestContext.RequireActor(ctx);
    var rid   = RequestContext.GetRequestId(ctx);

    await using var cmd = db.CreateCommand("SELECT fn_feature_delete(@e, @a, @r)");
    cmd.Parameters.AddWithValue("e", entityId);
    cmd.Parameters.AddWithValue("a", actor);
    cmd.Parameters.AddWithValue("r", rid);
    await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});
```

注意点:
- If-Match は要件にないので付けない（削除は最新版に対して行うのが暗黙）。将来必要なら追加

## テスト観点
- 0303: DELETE 後 current=0, history +1 (delete), audit +1
- 0304: 404 ケース
