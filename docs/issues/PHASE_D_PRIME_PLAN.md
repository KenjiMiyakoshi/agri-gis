# Phase D' Plan — 課題定義と採用案

Phase D' 着手前の課題分析と採用案。Phase E `docs/issues/PHASE_E_DESIGN_P.md` 流儀踏襲。

## 0. 出発点

Phase D 完了で GeoServer 同梱 + サーバラスタタイル経路が、Phase E 完了で `layer_style_version` 履歴管理 + asOf タイルが揃った。Phase E 動作確認の過程で、ユーザーから 1 件の課題と 4 件の運用 UX 要望が出た:

1. **(課題)** SLD を `PUT /api/admin/layers/{id}/style` で更新しても WebGIS では古いタイルが見える → **`sld_cache_busting.md` メモリに記録、Phase D' 1 件目**
2. **(要望)** 管理者が SLD を手で編集 (テキストエディタで XML 書く) する以外の手段が無い、UI が欲しい
3. **(要望)** 数値属性をベースに「Quantile (4 分位) で配色」みたいなことを自動でやりたい
4. **(要望)** 属性編集が 1 件単位、複数選択しても N 回 PATCH しないといけない
5. **(要望)** 編集が反映されたか確認するのに F5 や layer 切替が必要、自動で見たい

## 1. 課題 1: SLD cache busting

### 1.1 症状

- `tools/poc/GeoServerCheck/sld/default.sld` を更新 + GeoServer REST PUT
- WebGIS で同じ layer を見ても古い PointSymbolizer (赤丸) が Polygon に重なって表示
- 「過去時点モード ON」(`?asOf=` 付与) では新 SLD が反映 (`no-store` 経路)
- WinForms 再起動 + WebView2 cache 物理削除 で消える

### 1.2 原因

`api/Endpoints/TilesEndpoints.cs` の `TileFileResult`:

```csharp
httpContext.Response.Headers.CacheControl = _noStore
    ? "no-store, no-cache, must-revalidate"
    : "max-age=3600, public";
```

asOf 無しの場合 `max-age=3600, public` を返す。WebView2 はこれを尊重し、max-age 内は条件付きリクエストすら送らない。SLD 更新は GeoServer 側で起きるためクライアントは検知できない。

### 1.3 採用案

**案 A: タイル URL に `?sv={styleVersion}` 付与 + `Cache-Control` を `max-age=86400, immutable` に強化**

- 理由: URL に style version が入る前提で長期キャッシュ (1 日) + immutable hint で再要求も発生しない
- SLD 更新で `style_version` が +1 → URL が変わる → WebView2 がキャッシュミス → 新タイル取得
- 既存の Phase D `Cache-Control: max-age=3600` (再要求発生) を上回るキャッシュ効率
- 実装コスト極小 (`LayerDto` に `styleVersion` フィールド + `setBaseLayerSource` の URL 構築のみ)

落選:
- 案 B `no-cache, must-revalidate + ETag`: ブラウザは max-age なしで毎回条件付きリクエスト → GeoServer 再ラスタライズ多発、QPS スケール阻害
- 案 C `fetch cache: 'no-store'`: WebView2 キャッシュを一律無効化、テーマ切替時の体感速度低下

### 1.4 影響範囲

- DB: `layer_style_version` (既存、Phase E E103 で導入済)
- API: `LayerDto`, `AdminLayerDto` に `styleVersion`、`TilesEndpoints` の `Cache-Control` 文字列
- WebGIS: `setBaseLayerSource` の URL 構築、`LayerDto` 型定義、`loadFeatures` シグネチャ
- WinForms: 影響なし (タイルは WebView2 経路、API 直叩きはしない)

## 2. 課題 2-3: カスタム theme + カラーランプ UI

### 2.1 現状

- Phase D D202 で SLD theme 設定機能 (`PUT /api/admin/layers/{id}/style`) を実装済
- ただし管理者向け UI は無い、XML を直接 PUT body に書くか PowerShell スクリプトで
- 数値属性ベースの配色は手動で `<ogc:PropertyIsLessThan>` フィルタを並べる必要

### 2.2 採用案

**案 A: WebGIS の admin 画面に Monaco エディタ + ライブプレビュー + カラーランプ UI を 1 画面にまとめる**

- 単一 HTML エントリ `admin-style.html` (一般 WebGIS bundle と分離)
- 左ペイン: Monaco editor (Monaco loader を CDN 経由で動的 import)
- 右ペイン: 専用 OpenLayers map (preview)
- 上部: layer 選択 + theme 名選択 + 保存ボタン
- 下部: カラーランプ generator (属性選択 → bins 設定 → 配色プリセット → SLD 自動生成 → Monaco に流し込み)

落選:
- WinForms 内で SLD エディタ実装: ColorPicker / XML エディタを WinForms で書くのは煩雑、WebGIS 側に既に OL が居るので preview 統合が自然
- React/Vue 等のフレームワーク導入: 既存 WebGIS は素 TypeScript + OL、フレームワーク導入は overkill

### 2.3 影響範囲

- API: `GET /api/admin/layers/{id}/attributes/{field}/stats?bins=N&method=quantile|equal` 新設 (D'105)
- WebGIS: `webgis/src/admin/styleEditor.ts`, `webgis/src/admin/colorRamp.ts`, `admin-style.html` (新規エントリ)
- WinForms: 「テーマ編集を開く」ボタン (`http://localhost:5173/admin-style.html?layerId=N` を WebView2 で開く)
- `SldXmlBuilder.cs` 拡張: `colorRamp` を受領して N 段 Rule を生成

## 3. 課題 4: Batch update API

### 3.1 現状

`PATCH /api/features/{entityId}` 1 件単位。10 件選択して保存するときに 10 回 HTTP 呼ぶ必要。WAN 越しでは遅延倍化。

### 3.2 採用案

**案 A: `POST /api/features:batch` (all-or-nothing) + DB 関数 `fn_feature_batch_update`**

- リクエスト: `{ entityIds: [], attributesPatch: {}, ifMatchVersions: [] }`
- DB 関数で entityId × ifMatchVersion を全件突合、1 件でも mismatch → トランザクション全体を rollback
- レスポンス: 成功時に各 entity の new version、失敗時に mismatch した entityId を 409 で
- 既存 PATCH 1 件版は残置 (一括化は client 都合、サーバは両方対応)

落選:
- partial success (一部成功で返す): 半端な状態が監査しにくい、Phase A/E の atomic 路線と矛盾
- 並列 PATCH 1 件版 N 回: クライアント側並列化はネットワークラウンドトリップは減らせるが、トランザクション一貫性が無い

### 3.3 影響範囲

- DB: `0F01_fn_feature_batch_update.sql` 新規 (geometry は触らず属性のみ N 件まとめ更新)
- API: `POST /api/features:batch` (FeatureEndpoints.cs)、DTO 3 本
- WebGIS: `webgis/src/api/client.ts` に `postFeatureBatch`
- WinForms: `AttributeEditorControl` に「複数選択中の N 件まとめて編集」モード追加

## 4. 課題 5: 編集→無効化通知 (SSE)

### 4.1 現状

属性編集後、WebGIS は変更を検知できない。「ユーザーが手動で layer 切替 or F5」しないと反映されない。

### 4.2 採用案

**案 A: PostgreSQL `LISTEN/NOTIFY` + Server-Sent Events (SSE)**

- 既存 7 関数 (`fn_feature_insert/update/delete/fn_layer_style_upsert/fn_layer_update/fn_layer_delete_v2/fn_layer_schema_upsert`) に `pg_notify('agri_gis_layer_invalidate', 'layerId=N;reason=feature|style;styleVersion=M')` を追加
- API は永続接続 `LISTEN agri_gis_layer_invalidate` で購読
- `GET /api/events/layers/{layerId}/stream` (SSE) で WebGIS に push
- WebGIS は `EventSource` で購読、invalidation で `setBaseLayerSource` 再呼び出し → URL に新 `?sv=` → 自動 cache bust

落選:
- WebSocket: 双方向通信不要、SSE で十分。HTTP/1.1 で完結する分インフラ単純
- ポーリング (5s 間隔): 編集即時反映の体感が劣る、無駄な QPS

### 4.3 影響範囲

- DB: `0F02_notify_invalidation.sql` (既存 7 関数に `pg_notify` 追加、CREATE OR REPLACE で適用)
- API: `EventsEndpoints.cs` 新規 + Npgsql の `LISTEN` connection 1 本維持
- WebGIS: `webgis/src/controllers/eventStream.ts` + 既存 `MapContext` への配線
- WinForms: `LayerEventListener` クラス新規 (MainForm からは依存注入で受領、H5 リファクタを意識して切り出す)

## 5. 5 件まとめての一貫性

すべて **`layer_style_version.style_version` を URL/イベントに伝搬する** という 1 本のレールで動く:

- (1) SLD 更新 → `style_version+1` → URL に `?sv=` 伝搬 → cache bust
- (2)(3) admin UI で SLD 編集 → 同 PUT 経路 → `style_version+1` → preview map 自動再描画
- (4) batch update → `audit_log` 配線は既存通り
- (5) 編集完了 → `pg_notify` → SSE → 各クライアント受領 → `setBaseLayerSource` 再呼び出し → URL に新 `?sv=` → cache bust

つまり (5) のイベント通知ですべての更新が伝搬する仕組みになる。

## 6. 残課題 (Phase D'' 候補)

- **WMS GetFeatureInfo 統合**: Phase E' で「履歴 attribute 含めて hover で表示」要件が出るので E' Plan に EWGFI として再起票推奨
- **MapProxy 中間キャッシュ**: 本番 QPS 観測後
- **SldXmlBuilder 拡張**: D'205 でカテゴリ/カラーランプを実装するが、PointSymbolizer / TextSymbolizer (ラベル表示) は未実装。D'' か E' で扱う
- **SSE のスケール**: 1 API インスタンス前提、本番複数 API なら Redis pub-sub
- **本番 helm chart**: Phase H6

## 関連

- `PHASE_D_PRIME_INDEX.md`
- `PHASE_D_PRIME_WAVE_PLAN.md`
- `PHASE_D_PRIME_ISSUES_INDEX.md`
