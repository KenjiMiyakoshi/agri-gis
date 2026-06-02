# Phase D Design 案 A — GeoServer 同梱 + MapProxy キャッシュ (採択ベース)

`PHASE_D_PLAN.md` §3.1 の 3 候補のうち、Plan エージェント推奨案。`PHASE_D_DESIGN_P.md` の採択ベース。

## 1. アーキテクチャ

```
┌────────────┐    ┌───────────────────────────────────────────────┐
│  Browser   │ ←→ │ WebGIS (OpenLayers)                            │
│ (WebView2) │    │  - TileLayer(/tiles/{l}/{t}/{z}/{x}/{y}.png)   │
│            │    │  - TileLayer(/tiles/selection/{sid}/...)      │
└────────────┘    └───────────────────────────────────────────────┘
                              ↓ (Bearer JWT)
                  ┌─────────────────────┐
                  │  AgriGis.Api (.NET) │←─ POST /selection (sid 発行)
                  │  - Tile reverse-proxy │  GET /tiles/* (proxy)
                  │  - Admin theme CRUD  │  GET/PUT /admin/layers/{id}/style
                  └─────────────────────┘
                          ↓ internal (basic auth on docker network)
                  ┌──────────────────────┐    ┌─────────────────┐
                  │   GeoServer          │ ←→ │   PostGIS 16    │
                  │   - WMS/WMTS         │    │  feature_current │
                  │   - data_dir/styles  │    │  layers          │
                  │   - JNDI to postgis  │    │  selection_sets  │
                  └──────────────────────┘    └─────────────────┘
                          ↑
                  (Phase D' で MapProxy 永続キャッシュ層を挿入予定)
```

## 2. コンポーネント

### 2.1 GeoServer (Docker Compose 同梱、dev のみ)

- イメージ: `kartoza/geoserver:2.25.x` (PostGIS JNDI 同梱、コミュニティ標準)
- ポート: `8080` (Docker 内部のみ、API から basic auth で叩く。ホスト公開しない)
- データ: `geoserver/data_dir/` を bind mount。初期 `workspaces/agrigis/` + `datastores/postgis_jndi/` + `styles/` + `layers/` を git 管理
- 起動順: PostGIS 待機 (`depends_on.postgis.condition: service_healthy`)
- ヘルスチェック: `/geoserver/web/` HTTP 200

### 2.2 API tile proxy

- `GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png`
- API 内 `TilesEndpoints.cs` で Bearer JWT → GeoServer basic auth に変換
- 内部実装: `HttpClient` で `http://geoserver:8080/geoserver/agrigis/wms?...&LAYERS=l_{layerId}&STYLES=t_{theme}&BBOX=...&FORMAT=image/png&...` を叩いて pipe
- キャッシュ: HTTP 経由で `Cache-Control: max-age=3600, public` を返す。MapProxy は Phase D' 導入時にここを差し替え

### 2.3 selection raster overlay

- `POST /api/selection { entityIds: [], colorHex?: "#FFEB3B" } → 201 { sid: "uuid", ttl: "session" }`
  - DB 書き込み: `INSERT INTO selection_sets(sid, user_id, entity_ids, color_hex, created_at)`
  - user_id は `ICurrentUser.UserId` から取得 ([[A202]] と整合)
- `GET /tiles/selection/{sid}/{z}/{x}/{y}.png`
  - API 内で sid → user_id → entity_ids[] を取得し GeoServer に `CQL_FILTER=entity_id IN (...)` で 1 枚 PNG 生成依頼
  - sid 発行ユーザ以外がアクセス → 403
- sid TTL: ユーザログアウト時に `user_sessions.deleted_at` を埋め、関連 `selection_sets` を cascade 削除

### 2.4 theme (SLD) 管理

- `layers.style_json JSONB` に SLD ベースの JSON 表現を保持
  - 例: `{ "version": 1, "themes": { "default": {...}, "byOwner": {...} } }`
- `GET /api/admin/layers/{id}/style` で読み取り
- `PUT /api/admin/layers/{id}/style` で書き換え + GeoServer REST API (`/geoserver/rest/styles/...`) に SLD POST
- 初期 SLD は WD1 で `geoserver/data_dir/styles/default.sld` に置き、`layers.style_json` のデフォルト値とする

## 3. データフロー (4 系統、scale_target_and_server_side_rendering.md と整合)

| 操作 | フロー |
|---|---|
| 通常表示 | OL TileLayer → `GET /tiles/{l}/{t}/{z}/{x}/{y}.png` → API → GeoServer → PostGIS SELECT → ラスタライズ → PNG |
| theme 切替 | WinForms → bridge `theme_change` → WebGIS が新 theme で TileLayer 差替 |
| 選択ハイライト | OL clickEvent → `POST /api/selection` → sid → OL TileLayer(`/tiles/selection/{sid}/...`) を overlay 追加 |
| 編集モード (稀) | OL → `GET /api/features/{id}` → 一時 VectorSource → PATCH → API が該当 entity bbox を MapProxy invalidate (Phase D' 課題、Phase D は手動リロード) |

## 4. DB スキーマ追加

### 4.1 `layers.style_json JSONB`

```sql
ALTER TABLE layers
  ADD COLUMN style_json JSONB NOT NULL DEFAULT '{}'::jsonb;
COMMENT ON COLUMN layers.style_json IS
  'WD2 で API 経由更新、WD1 default SLD を JSON 化した初期値を入れる';
```

### 4.2 `selection_sets`

```sql
CREATE TABLE selection_sets (
  sid          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id      UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  entity_ids   UUID[] NOT NULL,
  color_hex    TEXT NOT NULL DEFAULT '#FFEB3B',
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_selection_sets_user_id ON selection_sets(user_id);
```

GIST index は **不要** (entity_ids IN クエリは GeoServer の CQL_FILTER 経由で発火する小規模配列展開)。

### 4.3 `user_sessions` (sid lifecycle 用)

```sql
CREATE TABLE user_sessions (
  session_id   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id      UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  jwt_jti      TEXT UNIQUE NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  deleted_at   TIMESTAMPTZ
);
```

JWT issuance 時に `user_sessions` レコードを INSERT。logout で `deleted_at` を埋める。`selection_sets.user_id` ではなく `session_id` を FK にする案もあるが、`one selection per session` と `multi selection per user` の判断を Phase D で先に decideせずに済むよう、Phase D は `user_id` 単独 FK にする (Phase D' で session FK に narrowing 可能)。

## 5. API endpoint まとめ

| Method | Path | 認可 | 追加 (Phase D) |
|---|---|---|---|
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` | Bearer (admin/general/guest) | 新規 |
| POST | `/api/selection` | Bearer (admin/general) | 新規 |
| GET | `/tiles/selection/{sid}/{z}/{x}/{y}.png` | Bearer + sid owner | 新規 |
| GET | `/api/admin/layers/{id}/style` | Bearer admin | 新規 |
| PUT | `/api/admin/layers/{id}/style` | Bearer admin | 新規 |
| GET | `/api/features?layerId=` | (廃止) | **Sunset → 410 (WD3)** |
| GET | `/api/features/{entityId}` | Bearer (admin/general/guest) | 既存 (継続) |
| PATCH | `/api/features/{entityId}` | Bearer (admin/general) | 既存 (継続) |
| GET | `/api/layers` | Bearer | 既存 (style_json 列追加) |

theme 切替の role 制限は未確定 (Plan 工程の未確定 1 件)。WD2 で決定するが、暫定として admin/general/guest 全員が theme 切替可能 (UI 制限なし)、admin のみ style_json 編集可能とする。

## 6. WebGIS 変更

### 6.1 mapInit.ts

```ts
// 旧: vectorLayer のみ
const vectorLayer = new VectorLayer({ source: vectorSource });

// 新: tileLayer 主役 + selection overlay (sid あり時のみ)
const baseTileLayer = new TileLayer({
  source: new XYZ({
    url: `${apiBase}/tiles/${currentLayerId}/${currentTheme}/{z}/{x}/{y}.png`,
    tileLoadFunction: (tile, src) => fetchTileWithAuth(tile, src, jwt),
  }),
});
const selectionOverlay = new TileLayer({ source: null });  // sid 確定で source 設定
```

### 6.2 selection.ts

- `singleclick` でクリック位置の `GET /tiles/selection/preview?bbox=&entityId=` を叩く (現状の `features_clicked` 経路と互換)。entity_id 確定後に `POST /api/selection` で sid 取得 → selectionOverlay の source 差替

### 6.3 bridge messages

| envelope | direction | payload |
|---|---|---|
| `theme_change` | WinForms → WebGIS | `{ layerId, theme }` |
| `selection_overlay_ready` | WebGIS → WinForms | `{ sid, count }` (sid を渡すと WinForms が AttributeEditor 一括モードに) |
| `features_selected` | WebGIS → WinForms | `{ entityIds[], sid }` (新規、旧 `feature_clicked` 単数を配列化) |
| `feature_clicked` | (廃止) | WD3 で削除 |

## 7. WinForms 変更

| ファイル | 変更 |
|---|---|
| `MainForm.cs` | `OnBridgeMessage` で `feature_clicked` 削除 → `features_selected` 配列受領。1 件モード/N 件モードで AttributeEditor 切替 |
| `AttributeEditorControl.cs` | N 件モード追加 (`LoadFeatures(entityIds[])`) — 編集 UI は disable、属性閲覧のみ。N 件編集 (一括) は Phase D' |
| `ApiClient.cs` | `GetFeaturesAsync` 削除、`CreateSelectionAsync(entityIds[])` 新規追加、`UpdateLayerStyleAsync(layerId, styleJson)` 新規追加 |
| `MainForm` 上部 ComboBox | theme 切替 UI 追加 (現在のレイヤの style_json.themes キー一覧を表示) |

## 8. テスト戦略

### 8.1 api.tests

- 既存 `?layerId=` 依存テスト書き換え:
  - `GET /api/features?layerId=` を期待していたテストを `GET /api/features/{entityId}` ループ or DB 直接 SELECT に変更
  - 影響範囲: `InsertInvariantTests` / `UpdateInvariantTests` / `DeleteInvariantTests` 等 (要 grep 計上 in WD0)
- 新規:
  - `TilesProxyTests`: JWT validation + GeoServer モック (WireMock.NET) で URL 構築検証
  - `SelectionEndpointTests`: sid 発行 → 別ユーザは 403 / 発行者は 200 / TTL (logout で cascade 削除)
  - `AdminLayerStyleTests`: style_json CRUD + admin role gate

### 8.2 webgis (Vitest)

- 新規:
  - `tileLayer.test.ts`: URL 構築 + JWT injection
  - `selection.test.ts`: クリック → POST → overlay 差替シーケンス

### 8.3 windos-app.tests

- 新規:
  - `MainFormThemeChangeTests`: bridge `theme_change` 発火
  - `AttributeEditorMultiModeTests`: N 件モード disable 状態

### 8.4 性能 smoke

- 50 万件 layer fixture + z=15 タイル × 5 リクエスト平均 < 500ms (cold cache)、< 50ms (warm cache)
- 失敗閾値 > 2s なら GeoServer index 設計を見直し

## 9. リスク (案 A 固有)

| # | リスク | 対応 |
|---|---|---|
| A1 | GeoServer GPL ライセンスとプロセス分離 | Docker 別コンテナ、API ↔ GeoServer は HTTP のみ。GPL 影響なし |
| A2 | SLD 学習コスト | WD0 で base + theme 1 種 = 2 SLD 作成、WD5 docs に 5 例パターン集 |
| A3 | docker-compose 起動時間増 | api.tests は HttpClient モック化 (R2 と統合) |
| A4 | GeoServer の zero-config 起動失敗 | WD0 PoC で要件確認 (PostgreSQL JNDI + data_dir bind mount) |
| A5 | tile cache key 設計 (theme,z,x,y) で N テーマ × N タイル | Phase D は GeoServer 内部キャッシュのみ、MapProxy は Phase D' |

## 10. 採用度

`PHASE_D_DESIGN_P.md` で **採択**。本ドキュメントの構成・データフロー・スキーマをそのまま採用、API endpoint を Issue 化。

## 11. 関連ドキュメント

- `PHASE_D_PLAN.md`: Plan 工程
- `PHASE_D_DESIGN_B.md`: 落選案 B (MapServer)
- `PHASE_D_DESIGN_C.md`: 落選案 C (自前 SkiaSharp)
- `PHASE_D_DESIGN_P.md`: 採用案 (本案ベース)
- `scale_target_and_server_side_rendering.md` (MEMORY.md)
