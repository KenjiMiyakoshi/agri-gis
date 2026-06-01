# 0210: `POST /api/features` 実装

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0204, 0107 |
| Blocks | なし |

## 概要
新規フィーチャを作成する `POST /api/features` を実装する。属性スキーマ違反は 422 + errors[]。

## 背景・目的
案 B' の書き込み起点。`fn_feature_insert` を呼び、その前段で属性スキーマ違反を API 層で検出する（必須欠落・型不一致）。

## スコープ
### 含む
- `POST /api/features`
- リクエストボディ: `CreateFeatureRequestDto { layerId, geometry, attributes }`
- レスポンス: 201 + `Location` ヘッダ + `{ featureId, entityId, version, attributesSchemaVersion }`
- X-Actor 必須
- 属性スキーマバリデーション: layers.schema_json を読み、`required` が欠落 → 422 / 型不一致 → 422
- `entityId` はサーバ生成 (Guid.NewGuid())
- `fn_feature_insert` 呼び出し
- 属性バリデーションは `Core/AttributeValidator` のような static ヘルパに切り出して再利用しやすくする（API 内部のもの。WinForms 側は別実装）

### 含まない
- batch insert
- 属性の高度なバリデーション (regex, range)

## 受け入れ条件 (Acceptance Criteria)
- [ ] 成功で 201, Location: `/api/features/{entityId}`, body に featureId/entityId/version=1
- [ ] X-Actor 無しで 400
- [ ] 必須属性欠落で 422 + errors[]
- [ ] 型不一致 (schema が `type:"string"` に number を渡す等) で 422 + errors[]
- [ ] 存在しない layerId で 404
- [ ] geometry が不正 GeoJSON で 400

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (追加)
- `D:\proj\agri-gis\api\Validation\AttributeValidator.cs` (新規)

## 実装ノート
```csharp
// Validation/AttributeValidator.cs
public static class AttributeValidator
{
    public static IReadOnlyList<AttributeErrorDto> Validate(
        LayerSchemaDto schema,
        IReadOnlyDictionary<string, JsonElement> attrs)
    {
        var errors = new List<AttributeErrorDto>();
        foreach (var f in schema.Fields)
        {
            var has = attrs.TryGetValue(f.Key, out var val);
            if (f.Required && (!has || val.ValueKind == JsonValueKind.Null))
            {
                errors.Add(new(f.Key, "required", $"{f.Key} is required"));
                continue;
            }
            if (has && val.ValueKind != JsonValueKind.Null)
            {
                var typeOk = f.Type switch
                {
                    "string" => val.ValueKind == JsonValueKind.String,
                    "number" => val.ValueKind == JsonValueKind.Number,
                    "boolean" => val.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    _ => true
                };
                if (!typeOk)
                    errors.Add(new(f.Key, "type_mismatch", $"{f.Key} expects {f.Type}"));
            }
        }
        return errors;
    }
}
```

```csharp
group.MapPost("/",
    async (CreateFeatureRequestDto req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var actor = RequestContext.RequireActor(ctx);
    var rid   = RequestContext.GetRequestId(ctx);

    // schema を取る
    LayerSchemaDto schema; int schemaVersion;
    await using (var c = db.CreateCommand("SELECT schema_version, schema_json FROM layers WHERE layer_id=@id"))
    {
        c.Parameters.AddWithValue("id", req.LayerId);
        await using var rr = await c.ExecuteReaderAsync();
        if (!await rr.ReadAsync()) throw new NotFoundException($"layer {req.LayerId}");
        schemaVersion = rr.GetInt32(0);
        schema = JsonSerializer.Deserialize<LayerSchemaDto>(rr.GetString(1), JsonOpts)!;
    }

    var errs = AttributeValidator.Validate(schema, req.Attributes ?? new());
    if (errs.Count > 0) throw new ValidationException(errs);

    var entityId = Guid.NewGuid();
    await using var cmd = db.CreateCommand(
        "SELECT fn_feature_insert(@l, @e, @g, @a::jsonb, @act, @rid)");
    cmd.Parameters.AddWithValue("l", req.LayerId);
    cmd.Parameters.AddWithValue("e", entityId);
    cmd.Parameters.AddWithValue("g", req.Geometry.GetRawText());
    cmd.Parameters.AddWithValue("a", JsonSerializer.Serialize(req.Attributes ?? new(), JsonOpts));
    cmd.Parameters.AddWithValue("act", actor);
    cmd.Parameters.AddWithValue("rid", rid);
    var featureId = (long)(await cmd.ExecuteScalarAsync())!;

    return Results.Created($"/api/features/{entityId}",
        new { featureId, entityId, version = 1, attributesSchemaVersion = schemaVersion });
});
```

注意点:
- `Geometry` は `JsonElement` で受けて `GetRawText()` で TEXT として関数に渡す
- 不正な GeoJSON は PostGIS が `XX000` などで例外、ProblemDetailsMiddleware で 400 にマップ（必要なら専用マップ追加）

## テスト観点
- 0303: INSERT 後 current=1, history=0, audit=+1
- 0304: required 欠落で 422, errors[0].attributeKey が "name" 等
