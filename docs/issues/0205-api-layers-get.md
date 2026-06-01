# 0205: `GET /api/layers` 拡張 (schema_json 含める)

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0202, 0102 |
| Blocks | 0206 |

## 概要
`GET /api/layers` を `LayerDto` ベースに移行し、`schema_json` / `schema_version` を含めて返す。

## 背景・目的
案 B' は WebGIS / WinForms 双方が起動時にレイヤ一覧と各レイヤの schema を欲しがる。1 ラウンドトリップで取れるよう、`/api/layers` に schema を載せる。

## スコープ
### 含む
- SELECT 文に `schema_json`, `schema_version` を追加
- `LayerDto`, `LayerSchemaDto`, `SchemaFieldDto` で構成
- camelCase の JSON で返す

### 含まない
- 個別 schema 取得 (0206)
- schema 更新 (0207)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `GET /api/layers` のレスポンスに `schemaVersion: 1` と `schema: { fields: [...] }` が含まれる
- [ ] schema_json が `{"fields":[]}` のレイヤは `schema: { fields: [] }` を返す
- [ ] 既存のキー (`layerId`, `layerName`, ...) は維持

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\LayerEndpoints.cs` (変更)

## 実装ノート
```csharp
group.MapGet("/", async (NpgsqlDataSource db) =>
{
    const string sql = @"
        SELECT layer_id, layer_name, layer_type, owner_org_id, is_shared, created_at,
               schema_version, schema_json
        FROM layers ORDER BY layer_id";

    await using var cmd = db.CreateCommand(sql);
    await using var r   = await cmd.ExecuteReaderAsync();
    var list = new List<LayerDto>();
    while (await r.ReadAsync())
    {
        var schemaJson = r.GetString(7);
        var schema = JsonSerializer.Deserialize<LayerSchemaDto>(schemaJson, JsonOpts)!;
        list.Add(new LayerDto(
            r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt32(3),
            r.GetBoolean(4),
            new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)),
            r.GetInt32(6),
            schema));
    }
    return Results.Ok(list);
});
```

注意点:
- `schema_json` の Deserialize が壊れたら 500 になる前提で OK（DB 側は INSERT 時に型 JSONB なので形は保証）
- `JsonOpts` は Program.cs で公開した `JsonSerializerOptions` を参照

## テスト観点
- 0301: schema 0 件で `fields: []` が返る
- 0303 連携: schema_upsert 後 schema_version が増えていることを `GET /api/layers` で確認
