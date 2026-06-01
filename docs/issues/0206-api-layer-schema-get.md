# 0206: `GET /api/layers/{layerId}/schema` 実装

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0205 |
| Blocks | 0207 |

## 概要
個別レイヤの現行スキーマを返す `GET /api/layers/{layerId}/schema` を実装する。

## 背景・目的
WinForms の属性編集パネルが起動時にレイヤ単位で schema を取りに来る想定。`/api/layers` 一括取得に加え、特定レイヤだけ取れるエンドポイントを用意して再取得コストを減らす。

## スコープ
### 含む
- `GET /api/layers/{layerId:int}/schema` → `LayerSchemaResponseDto { layerId, schemaVersion, schema: LayerSchemaDto }`
- 存在しない layerId は 404

### 含まない
- schema 更新 (0207)
- 履歴版 schema 取得（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `GET /api/layers/1/schema` で 200 + JSON
- [ ] `GET /api/layers/999/schema` で 404 + ProblemDetails
- [ ] レスポンス形 `{ layerId, schemaVersion, schema: { fields: [...] } }`

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\LayerEndpoints.cs` (追加)
- `D:\proj\agri-gis\api\Dto\LayerSchemaResponseDto.cs` (新規)

## 実装ノート
```csharp
public sealed record LayerSchemaResponseDto(int LayerId, int SchemaVersion, LayerSchemaDto Schema);
```
```csharp
group.MapGet("/{layerId:int}/schema", async (int layerId, NpgsqlDataSource db) =>
{
    const string sql = "SELECT schema_version, schema_json FROM layers WHERE layer_id = @id";
    await using var cmd = db.CreateCommand(sql);
    cmd.Parameters.AddWithValue("id", layerId);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) throw new NotFoundException($"layer not found: {layerId}");
    var schema = JsonSerializer.Deserialize<LayerSchemaDto>(r.GetString(1), JsonOpts)!;
    return Results.Ok(new LayerSchemaResponseDto(layerId, r.GetInt32(0), schema));
});
```

## テスト観点
- 0301: 存在 / 不存在
- 0304: 0207 と組み合わせて、PUT 後の `GET` が新 schema を返す
