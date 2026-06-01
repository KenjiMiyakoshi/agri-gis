# 0208: `GET /api/features` 拡張 (asOf, UNION ALL)

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 1d |
| Depends on | 0202, 0103, 0104 |
| Blocks | 0209 |

## 概要
既存 `GET /api/features?layerId=` を `LayerDto` ベースに移行し、`?asOf=YYYY-MM-DD` 引数を追加して過去断面のフィーチャを返せるようにする。

## 背景・目的
案 B' のバイテンポラル UI 要件。asOf 省略時は `feature_current` のみ、指定時は `feature_current ∪ feature_history` で valid_from <= asOf < valid_to の行を返す。

## スコープ
### 含む
- クエリ: `layerId` (必須), `asOf` (任意, ISO date `YYYY-MM-DD`)
- `asOf` 省略時: `feature_current` から `layer_id = @id` を返す
- `asOf` 指定時: `feature_current` と `feature_history` の UNION ALL から `valid_from <= asOf AND asOf < valid_to` を抽出
- ISO 8601 のフルタイムスタンプは受けない（フォーマット違反は 400）
- レスポンス: `FeatureCollectionDto`
- properties に `featureId/layerId/entityId/version/validFrom/validTo/attributesSchemaVersion/createdBy/updatedBy/createdAt/updatedAt + 属性`

### 含まない
- 個別取得 (0209)
- バウンディングボックスフィルタ（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `?layerId=1` のみで `feature_current` の全行を返す
- [ ] `?layerId=1&asOf=2026-01-01` で history を含む UNION 結果が返る（過去日付）
- [ ] asOf に `2026-01-01T00:00:00Z` のような ISO 8601 を渡すと 400 + ProblemDetails
- [ ] レスポンスは GeoJSON FeatureCollection 形 (`type: FeatureCollection`, `crs.name: EPSG:4326`)

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (変更)

## 実装ノート
```csharp
group.MapGet("/", async (int layerId, string? asOf, NpgsqlDataSource db) =>
{
    DateOnly? asOfDate = null;
    if (asOf is not null)
    {
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", out var d))
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("asOf", "format", "asOf must be YYYY-MM-DD")
            });
        asOfDate = d;
    }

    string sql = asOfDate is null
        ? @"SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                   attributes_schema_version, created_by, updated_by, created_at, updated_at,
                   attributes, ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj
              FROM feature_current
              WHERE layer_id = @id AND geom IS NOT NULL"
        : @"
            SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                   attributes_schema_version, created_by, updated_by, created_at, updated_at,
                   attributes, ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj
              FROM feature_current
              WHERE layer_id = @id AND geom IS NOT NULL
                AND valid_from <= @asof AND @asof < valid_to
            UNION ALL
            SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
                   attributes_schema_version, created_by, updated_by, created_at, updated_at,
                   attributes, ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj
              FROM feature_history
              WHERE layer_id = @id AND geom IS NOT NULL
                AND valid_from <= @asof AND @asof < valid_to";

    await using var cmd = db.CreateCommand(sql);
    cmd.Parameters.AddWithValue("id", layerId);
    if (asOfDate is not null) cmd.Parameters.AddWithValue("asof", asOfDate.Value);
    // ... reader -> FeatureDto -> FeatureCollectionDto
});
```

注意点:
- asOf 指定時は history 側に `archived_reason='delete'` の行も含まれることに注意。要件「過去日付指定で history 行を返す」を満たすので含めて OK
- `valid_to=9999-12-31` を「未来」として扱う前提でクエリが書ける

## テスト観点
- 0303: 初回 INSERT 後、asOf 無しで 1 件
- 0304: UPDATE 後、asOf=過去日付で旧版の図形が返る、asOf 無しで新版のみ
- 0304: asOf に ISO datetime を渡すと 400
