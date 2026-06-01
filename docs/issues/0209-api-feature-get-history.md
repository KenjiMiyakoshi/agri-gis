# 0209: `GET /api/features/{entityId}` + `GET /api/features/{entityId}/history`

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0208 |
| Blocks | なし |

## 概要
個別フィーチャの取得と履歴一覧の 2 エンドポイントを追加する。

## 背景・目的
WinForms 側で属性編集 / 履歴閲覧のために個別取得・履歴一覧が必要。`/api/features?layerId=` だけでは entity 単位の操作に不便。

## スコープ
### 含む
- `GET /api/features/{entityId:guid}?asOf=YYYY-MM-DD`
  - asOf 無し: feature_current から
  - asOf 有り: current + history の UNION から
  - 0 件は 404
- `GET /api/features/{entityId:guid}/history`
  - feature_history から `entity_id` でフィルタし `valid_to DESC` で返す
  - 空配列でも 200

### 含まない
- 履歴の paging（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 個別取得は `FeatureDto` を返す
- [ ] 個別取得で 0 件は 404
- [ ] 履歴一覧は `FeatureHistoryDto[]`
- [ ] 履歴は archived_reason / archived_by / archived_at を含む
- [ ] 一度も更新されていない entity の履歴は空配列

## 影響ファイル
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (追加)
- `D:\proj\agri-gis\api\Dto\FeatureHistoryDto.cs` (0202 で作成済み or ここで追加)

## 実装ノート
```csharp
public sealed record FeatureHistoryDto(
    long HistoryId,
    long FeatureId,
    int LayerId,
    string EntityId,
    int Version,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int AttributesSchemaVersion,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ArchivedAt,
    string ArchivedBy,
    string ArchivedReason,
    JsonElement Geometry,
    Dictionary<string, JsonElement> Attributes
);
```

```csharp
group.MapGet("/{entityId:guid}", async (Guid entityId, string? asOf, NpgsqlDataSource db) =>
{
    // 0208 と同じパターン。current + (asOf 指定時) history を UNION
    // 0 件は throw new NotFoundException("entity not found")
});

group.MapGet("/{entityId:guid}/history", async (Guid entityId, NpgsqlDataSource db) =>
{
    const string sql = @"
        SELECT history_id, feature_id, layer_id, entity_id, version,
               valid_from, valid_to, attributes_schema_version,
               created_by, updated_by, created_at, updated_at,
               archived_at, archived_by, archived_reason,
               ST_AsGeoJSON(ST_Transform(geom, 4326)) AS gj,
               attributes
          FROM feature_history
         WHERE entity_id = @id
         ORDER BY valid_to DESC, history_id DESC";
    // -> List<FeatureHistoryDto>
});
```

## テスト観点
- 0304: 一度 INSERT して GET、UPDATE 後 history が 1 件、再度 UPDATE で 2 件
- 0304: DELETE 後 current の GET が 404、history は archived_reason='delete' を含む
