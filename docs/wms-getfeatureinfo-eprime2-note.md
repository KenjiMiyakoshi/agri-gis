# WMS GetFeatureInfo Note (Phase E'' 送り)

Phase E' で本実装はしない。E'' 着手時の判断材料を残す。

## 背景

Phase D' で「WMS GetFeatureInfo 統合」を検討したが、現状の `/api/layers/{id}/at` (PostGIS `ST_DWithin`) で代替できるため D'' 送り → 今回 E'' へ再送。

## 現状の代替経路 (E' 時点)

```
WebGIS singleclick
  → POST /selection (entity_id 取得)
  → GET /tiles/selection/{sid}/... で overlay 表示
  → WinForms に bridge envelope 通知
  → ApiClient.GetFeatureAsync(entityId)  ← 属性取得
```

`GET /api/layers/{layerId}/at?x=&y=&tolerance=&asOf=` で entity_id を取得し、続けて `GET /api/features/{entityId}?asOf=` で attribute を取得。バイテンポラルも対応済 (Phase E WE2)。

## GetFeatureInfo の利点

- **1 リクエストで属性が取れる**: 現状 2 段 (at → feature) になっているのを 1 段に短縮
- **CQL_FILTER でバイテンポラル**: `valid_from <= @asof AND @asof < valid_to` を WMS request に直接付与可能
- **WMS GetMap と同じレンダリング**: ピクセル単位の visible feature 検出 (重なり順を考慮)、現状 `ST_DWithin` では tolerance による近傍検索

## GetFeatureInfo の難点

- **GeoServer 経由必須**: 現状 `/api/layers/{id}/at` は API 直接 → PostGIS。GetFeatureInfo は WMS proxy 経由 (Phase D Tiles 経路と同型) になる
- **応答が GML/JSON**: parse コードが必要
- **複数 layer 同時の info_format=application/json** はバージョン依存

## E'' 着手時の判断軸

1. **応答時間**: 現状 2 段経路 (at + feature) の合計レイテンシ vs GetFeatureInfo 1 リクエストの差を実測。差が 50ms 以上なら採用候補
2. **属性履歴対応**: 「クリックで履歴属性も hover 表示」要件が立ち上がった時点で、E'' で着手
3. **WMS GetMap と GetFeatureInfo の整合**: tile renderer (PNG) と info renderer の visibility 差異がないか確認

## 採用案候補

**案 A (E'' 着手時)**: 既存 `/api/layers/{id}/at` を残しつつ、新規 `/api/layers/{id}/info?x=&y=` を GeoServer GetFeatureInfo proxy として追加。フロント (WebGIS + WinForms) は両方使い分け可能にする。

**案 B**: `/at` を deprecation し、`/info` に統一。breaking change なので慎重。

## 関連

- `PHASE_E_PRIME_INDEX.md` (本ノートを参照)
- Phase D `docs/rendering.md` (Tiles proxy 経路の参考)
- `api/Endpoints/LayerEndpoints.cs` の `/at` endpoint 実装
