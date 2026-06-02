# agri-gis 描画アーキ (Phase D 以降)

Phase D で「クライアントベクタ (OpenLayers + VectorSource)」から「サーバラスタタイル (GeoServer 同梱)」に転換したアーキの解説。Phase A/B/C は前史 (`docs/PHASE_A_INDEX.md` / `PHASE_B_INDEX.md` / `PHASE_C_INDEX.md`)。

## 1. データフロー 4 系統

```
┌───────────────────────────────────────────────────────────────┐
│ WebGIS (OpenLayers, WebView2 内 IFRAME)                       │
│  ・OSM TileLayer  (base)                                       │
│  ・XYZ TileLayer  (/tiles/{layerId}/{theme}/{z}/{x}/{y}.png)  │
│  ・XYZ TileLayer  (/tiles/selection/{sid}/{z}/{x}/{y}.png)    │
└──────────────────┬────────────────────────────────────────────┘
                   ↓ (Bearer JWT, tileLoadFunction で fetch)
            ┌─────────────────────┐
            │  AgriGis.Api (.NET) │←─ POST /api/selection
            │  - Tile reverse-proxy │  POST /api/auth/logout
            │  - Admin theme CRUD  │  GET/PUT /api/admin/layers/{id}/style
            └────────┬────────────┘
                     ↓ basic auth (docker network)
              ┌──────────────────┐    ┌─────────────────┐
              │   GeoServer      │ ←→ │   PostGIS 16    │
              │   - WMS GetMap   │    │ feature_current  │
              │   - data_dir/    │    │ layers           │
              │   - SLD          │    │ selection_sets   │
              └──────────────────┘    │ user_sessions    │
                                      └─────────────────┘
```

### 1.1 通常表示 (頻度: 高)

| ステップ | 経路 |
|---|---|
| OL から GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` |
| API | `TilesEndpoints.cs` で Bearer JWT 検証 |
| API → GeoServer | basic auth で WMS GetMap (`?LAYERS=l_{layerId}&STYLES=t_{theme}&BBOX=...&SRS=EPSG:3857`) |
| GeoServer | data_dir/styles/{theme}.sld + PostGIS から SELECT → ラスタライズ → PNG |
| Cache | `Cache-Control: max-age=3600, public` (HTTP) + GeoServer 内部キャッシュ |

### 1.2 theme 切替 (頻度: 高)

WinForms ComboBox 操作 → bridge `theme_change` envelope → WebGIS が `setBaseLayerSource(ctx, layerId, theme)` で TileLayer の `XYZ` source を差替。再描画は OL が pan/zoom 範囲分のタイルを再取得 (URL の `{theme}` が変わる)。

サーバ側は `(layerId, theme, z, x, y)` を cache key にできるため、theme 切替後も他テーマのキャッシュは保たれる。

### 1.3 選択 raster overlay (頻度: 中)

```
1. WebGIS: ユーザがクリック / 矩形選択 → entity_ids 確定
2. WebGIS → API: POST /api/selection { entityIds, colorHex? }
3. API → DB: INSERT selection_sets (sid, user_id, session_id, entity_ids, color_hex) RETURNING sid
4. WebGIS: setSelectionOverlay(sid) で XYZ source を /tiles/selection/{sid}/... に差替
5. OL → API: GET /tiles/selection/{sid}/{z}/{x}/{y}.png (各タイル)
6. API → DB: SELECT entity_ids FROM selection_sets WHERE sid = $1 AND user_id = current_user
   - user_id 不一致は 403
7. API → GeoServer: GetMap with CQL_FILTER=entity_id IN (...) + env=highlight:{colorHex}
8. GeoServer: 該当 entity のみハイライト色で PNG ラスタライズ
9. WebGIS: TileLayer 上に overlay 表示
10. WebGIS → WinForms: features_selected + selection_overlay_ready envelope
```

`Cache-Control: no-cache, no-store` で sid 単位にキャッシュ無効化。`session_id` FK CASCADE により logout で自動削除。

### 1.4 編集モード (頻度: 稀)

```
1. WinForms: 編集対象 entity_id 確定
2. WinForms → API: GET /api/features/{entityId}?asOf=
3. WinForms: AttributeEditor で属性編集
4. WinForms → API: PATCH /api/features/{entityId} If-Match: version
5. API: バイテンポラル更新 (Phase A C1/C2 修復後の挙動)
6. (Phase D は手動リロード前提) WinForms 側からユーザに「保存しました、地図を再読込してください」
   - Phase D' 候補: 編集 bbox を含むタイルを MapProxy invalidate API で再生成
```

## 2. SLD パターン集 (5 例)

GeoServer の data_dir/styles/ に置く SLD ファイル。Phase D MVP (D203 `SldXmlBuilder`) は `default` パターンのみ自動生成、それ以外は手書き or Phase D' のカスタム theme UI で。

### 2.1 default (単色塗り)

```xml
<PolygonSymbolizer>
  <Fill>
    <CssParameter name="fill">#4CAF50</CssParameter>
    <CssParameter name="fill-opacity">0.5</CssParameter>
  </Fill>
  <Stroke>
    <CssParameter name="stroke">#1B5E20</CssParameter>
    <CssParameter name="stroke-width">1</CssParameter>
  </Stroke>
</PolygonSymbolizer>
```

### 2.2 byOwner (カテゴリ別カラー)

`PropertyIsEqualTo` Filter で attribute 別 Rule を並べる。`tools/poc/GeoServerCheck/sld/byOwner.sld` 参照。

### 2.3 byCropType (3 色固定カテゴリ)

A=赤、B=青、C=橙の 3 値。`byOwner.sld` を `crop_type` に置換するだけ。Phase D' でカテゴリ別カラー UI 化候補。

### 2.4 byArea (数値分類、5 階級 quantile)

```xml
<Rule>
  <Filter>
    <PropertyIsLessThan>
      <PropertyName>area_ha</PropertyName>
      <Literal>0.5</Literal>
    </PropertyIsLessThan>
  </Filter>
  <PolygonSymbolizer>...</PolygonSymbolizer>
</Rule>
```

階級数値はインポート時に統計から決定 (Phase D' で auto 化候補)。

### 2.5 labelOnly (アウトラインのみ + ラベル)

```xml
<PolygonSymbolizer>
  <Stroke><CssParameter name="stroke">#000000</CssParameter></Stroke>
</PolygonSymbolizer>
<TextSymbolizer>
  <Label><PropertyName>name</PropertyName></Label>
  <Font><CssParameter name="font-family">Noto Sans</CssParameter></Font>
</TextSymbolizer>
```

## 3. キャッシュ戦略

| 場面 | キャッシュ |
|---|---|
| 通常タイル `/tiles/{l}/{t}/{z}/{x}/{y}.png` | HTTP `Cache-Control: max-age=3600` + GeoServer 内部 (Phase D)、MapProxy 永続 (Phase D' で導入) |
| 選択 overlay `/tiles/selection/{sid}/{z}/{x}/{y}.png` | `no-cache, no-store` (短命 + ユーザ固有) |
| theme 切替時の無効化 | URL の `{theme}` が変わるので cache key 自体が別。旧 cache は LRU で alphabet 順 GC |
| 編集後の bbox 無効化 | Phase D 手動リロード前提、Phase D' で MapProxy seed/invalidate API |

## 4. Phase D' 申し送り

| 課題 | 補足 |
|---|---|
| MapProxy 永続キャッシュ | docker-compose に mapproxy service 追加、cache key 同じ |
| カスタム theme 編集 Web UI | 現状 API のみ (`PUT /api/admin/layers/{id}/style`)、SLD 直編集は data_dir/styles/ |
| 一括属性編集 `POST /api/features/batch-update` | 複数選択 → 共通属性編集 |
| 編集 → タイル無効化の WebSocket 通知 | Phase D は手動リロード |
| 数値分類 / カラーランプ生成 UI | Phase D MVP は手書き SLD |
| 本番 GeoServer の k8s 自動化 | Helm chart |
| WMS GetFeatureInfo 経由のクリック選択 | Phase D MVP は WinForms 側で entity_id 取得 |

## 5. 関連メモリ

- `scale_target_and_server_side_rendering.md`: 採用判断の論拠
- `selection_visualization_and_multi_select.md`: 選択 raster overlay 詳細
- `rendering_architecture_shift.md`: 前史と転換点
- `architecture.md`: WinForms+WebView2+WebGIS+API+PostGIS ハイブリッド構成

## 6. 関連ドキュメント

- `docs/PHASE_D_INDEX.md` — Phase D 高位サマリ
- `docs/issues/PHASE_D_DESIGN_P.md` — 採用案 (Picked)
- `docs/issues/PHASE_D_ISSUES_INDEX.md` — Issue 一覧
- `docs/deploy/geoserver-prod.md` — 本番別ホスト構成
- `tools/poc/GeoServerCheck/README.md` — PoC スケルトン
