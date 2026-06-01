# 0201: `MapGroup` 3 分割と `Endpoints/` 構造

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | なし |
| Blocks | 0202, 0203, 0205, 0206, 0207, 0208, 0209, 0210, 0211, 0212 |

## 概要
現在モノリシックな `Program.cs` を、`MapGroup` で `/api/layers`, `/api/features`, `/api/admin` の 3 つに分け、各グループを `Endpoints/` フォルダの拡張メソッドに切り出す。

## 背景・目的
案 B' は API が広がるので、Program.cs を肥大化させない。Minimal API の `MapGroup` + 拡張メソッドパターンに早期に揃える。

## スコープ
### 含む
- `api/Endpoints/LayerEndpoints.cs` (`MapLayerEndpoints(this RouteGroupBuilder)`)
- `api/Endpoints/FeatureEndpoints.cs`
- `api/Endpoints/AdminEndpoints.cs`
- `Program.cs` で 3 つの `MapGroup` を切って各拡張を呼ぶ
- 既存 `GET /api/layers`, `GET /api/features?layerId=` を `LayerEndpoints` / `FeatureEndpoints` に移植（**機能変更なし**、移すだけ）
- `GET /api/health` は `Program.cs` に残す

### 含まない
- DTO 化 (0202)
- ミドルウェア (0203)
- 新エンドポイントの中身 (0205 以降)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet build` が通る
- [ ] `dotnet run` 後、`curl http://localhost:5080/api/health` が `{"status":"ok"}`
- [ ] `curl http://localhost:5080/api/layers` が従来と同じ JSON 配列を返す
- [ ] `curl "http://localhost:5080/api/features?layerId=1"` が従来と同じ GeoJSON を返す
- [ ] `Program.cs` 行数が 60 行以下になる

## 影響ファイル
- `D:\proj\agri-gis\api\Program.cs` (変更)
- `D:\proj\agri-gis\api\Endpoints\LayerEndpoints.cs` (新規)
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (新規)
- `D:\proj\agri-gis\api\Endpoints\AdminEndpoints.cs` (新規)

## 実装ノート
```csharp
// Program.cs (抜粋)
var layers  = app.MapGroup("/api/layers");
var feats   = app.MapGroup("/api/features");
var admin   = app.MapGroup("/api/admin");

layers.MapLayerEndpoints();
feats.MapFeatureEndpoints();
admin.MapAdminEndpoints();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
```

```csharp
// Endpoints/LayerEndpoints.cs
namespace AgriGis.Api.Endpoints;

public static class LayerEndpoints
{
    public static RouteGroupBuilder MapLayerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (NpgsqlDataSource db) =>
        {
            // 既存 SELECT をそのまま移植
        });
        return group;
    }
}
```

注意点:
- `AdminEndpoints` は空でも OK（後続イシューで PUT スキーマを追加）
- ファイル先頭の `using` は `namespace AgriGis.Api.Endpoints;` に応じて整理

## テスト観点
- 0301 系で `/api/health`, `/api/layers`, `/api/features?layerId=1` が動くことを確認
