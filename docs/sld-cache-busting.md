# SLD Cache Busting (Phase D' D'1)

タイル URL に `layer_style_version.style_version` を載せて、SLD 更新時の WebView2 / ブラウザキャッシュ無効化を即時化する。

## 背景

Phase D D201 で実装したタイル経路:

```
GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png
→ Cache-Control: max-age=3600, public
```

`max-age=3600` は同一テーマ内の zoom/pan で同タイルが再要求されたときの GeoServer 負荷低減を狙ったもの。一方 SLD は GeoServer 側で更新されるためクライアントは検知できず、ブラウザは「最大 1 時間古いタイルを返す」状態になる。

Phase E 動作確認で実例発生:
- `tools/poc/GeoServerCheck/sld/default.sld` を更新 (geometryType フィルタで Point/Line/Polygon を分岐)
- GeoServer 側では新 SLD が反映 (直接 WMS GetMap で確認、9497 bytes Polygon-only PNG)
- WebGIS では旧 SLD のタイル (赤丸が Polygon 上に重なる) が表示
- 「過去時点モード ON」(`?asOf=`) では URL 変化で新 SLD 反映 (`no-store` 経路)

## 採用案: 案 A — URL に style_version 付与

タイル URL に `?sv={styleVersion}` クエリパラメータを付与する。

```
GET /tiles/1/default/12/3645/1612.png?sv=3
GET /tiles/1/default/12/3645/1612.png?sv=3&asOf=2025-01-01
```

- `sv` パラメータは **API ロジックに使わない** (`TilesEndpoints.cs` は無視する)。**URL 一意性のためだけに使う**
- SLD 更新 (`PUT /api/admin/layers/{id}/style`) → `fn_layer_style_upsert` で `style_version+1` → `GET /api/layers` レスポンスの `styleVersion` が +1 → WebGIS が次回 `setBaseLayerSource` で `?sv=N+1` を URL に組み込む → WebView2 がキャッシュミス → 新タイル取得

### Cache-Control 強化

URL に version が入る前提なので長期キャッシュ可:

```csharp
httpContext.Response.Headers.CacheControl = _noStore
    ? "no-store, no-cache, must-revalidate"
    : "max-age=86400, immutable";   // ← 24 時間 + immutable hint
```

`immutable` hint で再要求自体が抑制される (Chromium 系では F5 でも再要求しない、Ctrl+F5 強制リロードのみ再要求)。

## 落選案

### 案 B: `Cache-Control: no-cache, must-revalidate` + ETag

ブラウザは毎回条件付きリクエスト (`If-None-Match`) を送る:
- 304 Not Modified なら新タイル取得しない
- GeoServer SLD 更新後は ETag が変わって 200 で再ラスタライズ

問題:
- **GeoServer 側で ETag を返さないと意味がない** (WMS GetMap は ETag 出さない)
- API 側で `style_version + bbox + z/x/y` から ETag を計算する手間
- ブラウザは毎タイルで条件付きリクエスト → QPS 削減効果が薄い (条件付きでも HTTP リクエストは飛ぶ)

### 案 C: WebGIS の fetch に `cache: 'no-store'`

`tileLoadFunction` 内の fetch 呼び出しを:

```javascript
fetch(src, {
  headers: { Authorization: `Bearer ${token}` },
  cache: 'no-store'   // ← 追加
})
```

問題:
- **タイル全部に対して WebView2 キャッシュ無効化** → テーマ切替 (theme=A → B → A) で全タイル再要求発生、体感速度低下
- Phase D D201 の `max-age=3600` 設計と矛盾

## 詳細実装

### DB (新規追加なし)

Phase E E103 で `layer_style_version` テーブル + `style_version` カラムが既に存在。

`GET /api/layers` のクエリで `layers.style_json` と一緒に active な `layer_style_version.style_version` を JOIN で取得:

```sql
SELECT l.layer_id, l.layer_name, l.layer_type, l.style_json,
       COALESCE(lsv.style_version, 1) AS style_version
  FROM layers l
  LEFT JOIN layer_style_version lsv
    ON lsv.layer_id = l.layer_id
   AND lsv.valid_to = '9999-12-31'::date
 WHERE l.valid_to = '9999-12-31'::date
   AND l.deleted_at IS NULL
 ORDER BY l.layer_id
```

### API: LayerDto / AdminLayerDto

```csharp
public record LayerDto(
    int LayerId,
    string LayerName,
    string LayerType,
    JsonElement? StyleJson,
    int StyleVersion   // ← D'101 (WD'1) 追加
);
```

### API: TilesEndpoints

`?sv=` クエリパラメータを受領するが、API ロジックには使わない:

```csharp
group.MapGet("/{layerId:int}/{theme}/{z:int}/{x:int}/{y:int}.png",
    async (int layerId, string theme, int z, int x, int y,
           string? asOf, string? sv,  // ← sv 追加 (使わない)
           IHttpClientFactory httpClientFactory, ...) => {
        // sv は URL 一意性のため、ロジックには使わない
        ...
    });
```

`Cache-Control: max-age=86400, immutable` (D'102)。

### WebGIS: setBaseLayerSource

```typescript
export function setBaseLayerSource(
  ctx: MapContext,
  layerId: number,
  theme: string,
  asOf: string | null = null,
  styleVersion: number | null = null   // ← D'201 追加
): void {
  const params = new URLSearchParams();
  if (asOf) params.set('asOf', asOf);
  if (styleVersion !== null) params.set('sv', String(styleVersion));
  const qs = params.toString();
  const url = `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png${qs ? '?' + qs : ''}`;
  ...
}
```

`styleVersion` は `loadFeatures` 経由で渡される。`fetchLayers` のレスポンスの `LayerDto.styleVersion` を保持。

### Phase D' D'5 (SSE) との連動

SSE 経由で「style_version=3 → 4 になった」通知を受領したら、WebGIS は次のように動く:

1. `MapContext.currentStyleVersion = 4` を更新
2. `setBaseLayerSource(ctx, layerId, theme, asOf, 4)` を再呼び出し
3. URL が `?sv=3` から `?sv=4` に変わる
4. WebView2 がキャッシュミス
5. 新 SLD のタイル取得

## 受入条件

1. `GET /api/layers` レスポンスに `styleVersion` フィールドが含まれる
2. PUT `/api/admin/layers/{id}/style` 後、再度 `GET /api/layers` で `styleVersion` が +1
3. WebGIS でレイヤ表示 → URL に `?sv=N` が含まれる (DevTools Network で確認)
4. SLD 更新後、layer 再選択 (もしくは SSE 受領) で URL が `?sv=N+1` に変わる
5. WebView2 が新タイルを取得 (キャッシュミスを DevTools Network の `Size` 列で確認、`memory cache` でなく `200` returned)
6. `Cache-Control: max-age=86400, immutable` が応答ヘッダに含まれる (asOf 無し時)
7. `Cache-Control: no-store, no-cache, must-revalidate` が応答ヘッダに含まれる (asOf 有り時)

## テスト

- `LayersEndpointsStyleVersionTests` (`api.tests`): `GET /api/layers` の `styleVersion` が PUT style 後に +1
- `TilesEndpointsCacheControlTests` (`api.tests`): `Cache-Control` ヘッダ値の検証 (asOf 無し/有り 2 ケース)
- `layer.cacheBusting.spec.ts` (`webgis vitest`): `setBaseLayerSource` の URL に `?sv=` が含まれる

## 関連

- メモリ `sld_cache_busting.md`
- `docs/PHASE_D_PRIME_INDEX.md`
- `docs/rendering.md` (Phase D 経路)
- `docs/bitemporal-asof.md` (Phase E layer_style_version)
