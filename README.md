# agri-gis

PostGIS をバックエンドに据えた WebGIS 最小構成。

```
agri-gis/
├── docker-compose.yml      PostGIS + pgAdmin
├── db/init/                初期化SQL (起動時に自動実行)
│   ├── 001_init.sql        layers / feature_current
│   └── 002_seed.sql        表示確認用シード
├── api/                    ASP.NET Core Web API (.NET 8)
│   ├── /api/layers
│   └── /api/features?layerId=...
├── webgis/                 OpenLayers + TypeScript (Vite)
└── docs/
```

## 必要なもの

| ツール | バージョン |
| --- | --- |
| Docker Desktop | 任意 |
| .NET SDK | 8.0+ |
| Node.js | 20+ |

## 起動手順

### 1. PostGIS / pgAdmin を起動

```powershell
docker compose up -d
```

- PostGIS: `localhost:5432`
  - DB: `agri_gis` / user: `agri_user` / pass: `agri_pass`
- pgAdmin: <http://localhost:8081>
  - Email: `admin@example.com` / pass: `admin_pass`

`db/init/*.sql` はコンテナ初回起動時のみ自動実行されます。スキーマやシードを変更した場合は、ボリュームごと作り直してください。

```powershell
docker compose down -v
docker compose up -d
```

### 2. API を起動

```powershell
cd api
dotnet run
```

`http://localhost:5080` で待ち受けます。接続文字列は環境変数 `AGRI_GIS_DB` または `appsettings.json` の `ConnectionStrings:AgriGis` で上書きできます。

動作確認:

```powershell
curl http://localhost:5080/api/health
curl http://localhost:5080/api/layers
curl "http://localhost:5080/api/features?layerId=1"
```

`/api/features` は EPSG:4326 の GeoJSON `FeatureCollection` を返します。

### 3. WebGIS を起動

```powershell
cd webgis
npm install
npm run dev
```

<http://localhost:5173> を開く。Vite の dev server が `/api/*` を `http://localhost:5080` にプロキシするので、ブラウザからは同一オリジン扱いで API を叩けます。

## 画面の使い方

- 上部の **Layer** セレクトで `/api/layers` の一覧から表示レイヤを切り替え。
- **回転 (deg)** スライダで OpenLayers の `View.setRotation()` を操作 (−180°〜+180°)。地図側のショートカット (Shift + ドラッグ) で回した結果もスライダに反映されます。

## エンドポイント仕様

### `GET /api/layers`

```json
[
  {
    "layerId": 1,
    "layerName": "サンプル圃場",
    "layerType": "polygon",
    "ownerOrgId": null,
    "isShared": true,
    "createdAt": "2026-05-29T06:00:00"
  }
]
```

### `GET /api/features?layerId=<int>`

`feature_current.geom` を `ST_Transform(geom, 4326)` → `ST_AsGeoJSON` で整形し、`attributes` (JSONB) を `properties` にマージして返します。

```json
{
  "type": "FeatureCollection",
  "crs": { "type": "name", "properties": { "name": "EPSG:4326" } },
  "features": [
    {
      "type": "Feature",
      "geometry": { "type": "Polygon", "coordinates": [[[143.2, 42.91], ...]] },
      "properties": {
        "featureId": 1,
        "layerId": 1,
        "entityId": "…",
        "name": "A圃場",
        "crop": "じゃがいも"
      }
    }
  ]
}
```

## トラブルシュート

- **API が DB に繋がらない**: `docker compose ps` で `agri_postgis` が `healthy` か確認し、`5432` が他プロセスと衝突していないかチェック。
- **WebGIS に何も表示されない**: ブラウザの DevTools で `/api/features` のレスポンスを確認。`features: []` ならシードSQLが流れていないので `docker compose down -v` でボリュームを作り直す。
- **CORS エラー**: WebGIS を `npm run dev` 経由 (Vite プロキシ) で動かしているか確認。直接 `5080` を叩く場合は `api/Program.cs` の `WithOrigins` にオリジンを追加。
