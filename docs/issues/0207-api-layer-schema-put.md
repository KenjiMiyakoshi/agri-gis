# 0207: `PUT /api/admin/layers/{layerId}/schema` 実装

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0206, 0110 |
| Blocks | なし |

## 概要
レイヤの schema_json を丸ごと差し替える `PUT /api/admin/layers/{layerId}/schema` を実装する。

## 背景・目的
案 B' の admin 系。`fn_layer_schema_upsert` を呼び、新 schema_version を返す。X-Actor 必須。

## スコープ
### 含む
- `PUT /api/admin/layers/{layerId:int}/schema`
- リクエストボディ: `UpdateSchemaRequestDto { schema: LayerSchemaDto }`
- 関数 `fn_layer_schema_upsert(layerId, schema_json, actor)` を呼ぶ
- X-Actor 必須
- 200 + `{ layerId, schemaVersion: <new> }`
- 簡易バリデーション: `schema.fields` が null や空配列でも OK、各 field の key/type が空文字なら 422

### 含まない
- schema migration (既存フィーチャの再バリデーション)

## 受け入れ条件 (Acceptance Criteria)
- [ ] 成功で 200 + `{ layerId, schemaVersion }`
- [ ] X-Actor 無しで 400
- [ ] 存在しない layerId で 404
- [ ] `fields[i].key` が空で 422 + errors[]
- [ ] PUT 後、`GET /api/layers/{id}/schema` が新 schema を返す

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\AdminEndpoints.cs` (追加)
- `D:\proj\agri-gis\api\Dto\UpdateSchemaRequestDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\UpdateSchemaResponseDto.cs` (新規)

## 実装ノート
```csharp
public sealed record UpdateSchemaRequestDto(LayerSchemaDto Schema);
public sealed record UpdateSchemaResponseDto(int LayerId, int SchemaVersion);
```

```csharp
group.MapPut("/layers/{layerId:int}/schema",
    async (int layerId, UpdateSchemaRequestDto req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var actor = RequestContext.RequireActor(ctx);

    // 簡易バリデーション
    var errors = new List<AttributeErrorDto>();
    if (req.Schema?.Fields is null)
        errors.Add(new("schema.fields", "required", "fields is required"));
    else
        foreach (var (f, i) in req.Schema.Fields.Select((f, i) => (f, i)))
        {
            if (string.IsNullOrWhiteSpace(f.Key))
                errors.Add(new($"schema.fields[{i}].key", "required", "key is required"));
            if (string.IsNullOrWhiteSpace(f.Type))
                errors.Add(new($"schema.fields[{i}].type", "required", "type is required"));
        }
    if (errors.Count > 0) throw new ValidationException(errors);

    var schemaJson = JsonSerializer.Serialize(req.Schema, JsonOpts);

    await using var cmd = db.CreateCommand("SELECT fn_layer_schema_upsert(@id, @s::jsonb, @a)");
    cmd.Parameters.AddWithValue("id", layerId);
    cmd.Parameters.AddWithValue("s", schemaJson);
    cmd.Parameters.AddWithValue("a", actor);
    var newVersion = (int)(await cmd.ExecuteScalarAsync())!;
    return Results.Ok(new UpdateSchemaResponseDto(layerId, newVersion));
});
```

注意点:
- 既存フィーチャの再バリデーションは行わない（schema upgrade はあくまで以降の書き込みに影響）

## テスト観点
- 0304: 連続更新で schema_version が +1, +2, ... となり、layer_schema_version の旧行に valid_to が入る
- 0304: 不正 schema で 422 + errors[]
