# agri-gis Phase D イシュー一覧 (案 P)

`PHASE_D_DESIGN_P.md` 分割。`D1xx`=PoC/Infra/DB / `D2xx`=API / `D3xx`=WebGIS / `D4xx`=WinForms / `D5xx`=Tests / `D6xx`=Docs。S=0.5d / M=1d / L=1.5d。

## 一覧 (20 Issue / 約 11.0-11.5d)

| # | GH | タイトル | 工数 | 主担当 | 依存 |
|---|---|---|---|---|---|
| D100 | [#169](https://github.com/KenjiMiyakoshi/agri-gis/issues/169) | GeoServer 同梱 PoC (Gate、`tools/poc/GeoServerCheck/`) | M(1d) | infra | — |
| D101 | [#170](https://github.com/KenjiMiyakoshi/agri-gis/issues/170) | dev `docker-compose.yml` に geoserver サービス追加 + healthcheck | S(0.5d) | infra | D100 |
| D102 | [#171](https://github.com/KenjiMiyakoshi/agri-gis/issues/171) | DB migration 4 本 (`0D01-0D04`) + bootstrap | M(1d) | db | D101 |
| D103 | [#172](https://github.com/KenjiMiyakoshi/agri-gis/issues/172) | JWT 発行/検証に `sid_session` claim 追加 + `user_sessions` lifecycle | S(0.5d) | api | D101 |
| D201 | [#173](https://github.com/KenjiMiyakoshi/agri-gis/issues/173) | `GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png` proxy endpoint | S(0.5d) | api | D102,D103 |
| D202 | [#174](https://github.com/KenjiMiyakoshi/agri-gis/issues/174) | `POST /api/selection` + `GET/DELETE /tiles/selection/{sid}/...` | M(0.8d) | api | D102,D103 |
| D203 | [#175](https://github.com/KenjiMiyakoshi/agri-gis/issues/175) | admin theme CRUD + GeoServer REST 同期 (`IGeoServerStyleSync`) | M(0.6d) | api | D102 |
| D204 | [#176](https://github.com/KenjiMiyakoshi/agri-gis/issues/176) | `POST /api/auth/logout` + `selection_sets` cascade 削除 | S(0.3d) | api | D102,D103 |
| D205 | [#177](https://github.com/KenjiMiyakoshi/agri-gis/issues/177) | `GET /api/features?layerId=` に Sunset ヘッダ + `IApiClient.GetFeaturesAsync` 削除 | S(0.3d) | api | D201,D202 |
| D301 | [#178](https://github.com/KenjiMiyakoshi/agri-gis/issues/178) | WebGIS: `VectorLayer` → `TileLayer` 主役切替 + theme 切替 | M(0.8d) | webgis | D201,D203 |
| D302 | [#179](https://github.com/KenjiMiyakoshi/agri-gis/issues/179) | WebGIS: 選択 2 段パイプライン (OL Style 暫定 → サーバタイル差替) | M(0.7d) | webgis | D202,D301 |
| D303 | [#180](https://github.com/KenjiMiyakoshi/agri-gis/issues/180) | WebGIS: bridge envelope 拡張 + `?layerId=` を 410 化 | S(0.5d) | webgis | D205,D301,D302 |
| D401 | [#181](https://github.com/KenjiMiyakoshi/agri-gis/issues/181) | WinForms: `MainForm` bridge handler 更新 + `ApiClient` メソッド差替 | M(0.6d) | winforms | D303 |
| D402 | [#182](https://github.com/KenjiMiyakoshi/agri-gis/issues/182) | WinForms: `AttributeEditorControl` N 件モード | S(0.4d) | winforms | D401 |
| D501 | [#183](https://github.com/KenjiMiyakoshi/agri-gis/issues/183) | api.tests 新規 4 件 (tile proxy / selection / admin style / logout) | M(0.8d) | tests | D205,D401 |
| D502 | [#184](https://github.com/KenjiMiyakoshi/agri-gis/issues/184) | webgis vitest 新規 2 件 (tileLayer / selection) | S(0.4d) | tests | D303 |
| D503 | [#185](https://github.com/KenjiMiyakoshi/agri-gis/issues/185) | windos-app.tests 新規 2 件 (theme change / N 件モード) | S(0.4d) | tests | D402 |
| D504 | [#186](https://github.com/KenjiMiyakoshi/agri-gis/issues/186) | `?layerId=` 依存テスト書き換え + 50 万件性能 smoke | M(0.8d) | tests | D205,D501 |
| D601 | [#187](https://github.com/KenjiMiyakoshi/agri-gis/issues/187) | `docs/rendering.md` (Phase D アーキ解説) | S(0.3d) | docs | D504 |
| D602 | [#188](https://github.com/KenjiMiyakoshi/agri-gis/issues/188) | `docs/deploy/geoserver-prod.md` + `docs/PHASE_D_INDEX.md` 最終化 | S(0.3d) | docs | D504 |

クリティカルパス: D100 → D101 → D102 並列 → D202 → D301 → D302 → D303 → D401 → D402 → D504 → D601/D602 (≒ **9-10 営業日 + バッファ**)

ラベル: `phase:D` / `area:infra|db|api|webgis|winforms|tests|docs` / `wave:WD0`〜`wave:WD5` / `phase-d-prime-followup` (MapProxy / カスタム theme UI / batch-update 等の申し送り Issue)

全 PR `base=main` 固定 (MEMORY.md `stacked_pr_pitfall`)。

---

## Issue 詳細

### D100 GeoServer 同梱 PoC (infra/M/前提条件)

`tools/poc/GeoServerCheck/` 配下に最小 docker-compose + 検証スクリプトを置き、GeoServer + PostgreSQL JNDI + SLD パラメタライズ + CQL_FILTER 選択 raster の 3 要件を実機 PoC で確認する。Phase D 全体の着手前提条件。

受け入れ条件:
- `tools/poc/GeoServerCheck/docker-compose.yml` で `geoserver` + `postgis` の最小構成
- `tools/poc/GeoServerCheck/verify.sh` で curl による検証 6 ステップ自動化
- `agrigis` workspace + `postgis_jndi` datastore + `feature_current` layer 公開
- SLD 2 種 (`default.sld`, `byOwner.sld`) で配色変化を PNG で確認
- `CQL_FILTER=entity_id IN (...)` で 1000 件選択の透過 PNG 取得 + 応答時間計測
- `docs/issues/PHASE_D_D100_POC_RESULT.md` に go/no-go 判定を記録
- 既存 `api.tests` 全 green (Phase A/B/C 退行なし)
- `?layerId=` 依存テスト件数を grep 計上、PR description に記録

ブロッカー判定: PoC で 6 要件のうち 1 つでも未達成なら Phase D 着手中止し原因切り分けタスクを起票。

### D101 dev docker-compose.yml に geoserver サービス追加 + healthcheck (infra/S)

dev `docker-compose.yml` に `geoserver` サービスを追加。`kartoza/geoserver:2.25.x` イメージ、`data_dir` を `geoserver/data_dir/` に bind mount、`postgis` への JNDI 接続を環境変数で渡す。

受け入れ条件:
- `docker-compose.yml` に `geoserver` サービス定義 (image / ports / volumes / depends_on / healthcheck)
- `geoserver/data_dir/` に初期 workspace + datastore + 2 SLD を git 管理 (D100 PoC 成果を移植)
- `docker-compose up -d` で 60-90 秒以内に healthy
- `appsettings.json` に `GeoServer { BaseUrl, AdminUser, AdminPasswordEnv }` 追加
- 既存 `postgis` service が `service_healthy` で `geoserver` を待たせる
- README に dev 起動手順追記 (D602 で正式 docs 化)

### D102 DB migration 4 本 + bootstrap (db/M)

`0D01_layers_style_json.sql` + `0D02_user_sessions.sql` + `0D03_selection_sets.sql` + `0D04_selection_sets_session_link.sql` の 4 本を追加。down script も含む。

受け入れ条件:
- `db/migration/0D01-0D04*.sql` 4 本作成
- `db/migration/0D01-0D04_down*.sql` 4 本作成 (逆順で実行可能)
- `0D02_user_sessions.sql`: PK + user_id FK + jwt_jti UNIQUE + created_at + deleted_at
- `0D03_selection_sets.sql`: PK + user_id FK CASCADE + entity_ids UUID[] + color_hex + created_at
- `0D04_selection_sets_session_link.sql`: `selection_sets.session_id UUID NULL REFERENCES user_sessions(session_id) ON DELETE CASCADE`
- `0D01_layers_style_json.sql`: `layers.style_json JSONB NOT NULL DEFAULT '{}'`
- `Program.cs` の bootstrap で 4 migration 順次適用 (Phase A/B/C と同流儀)
- `psql` で `\d` 全テーブルを確認可能
- 既存 DB 整合性を破壊しない (既存 `layers` レコードは `style_json='{}'` で埋まる)

### D103 JWT 発行/検証に sid_session claim 追加 + user_sessions lifecycle (api/S)

`JwtService.IssueAccessToken` を拡張し、JWT 発行時に `user_sessions` テーブルに新規 session_id を INSERT、その値を `sid_session` claim に積む。`HttpContextCurrentUser` で claim を取得し `ICurrentUser.SessionId` で公開。

受け入れ条件:
- `IssueAccessToken(...)` の引数に `Guid sessionId` 追加 (or 内部で `Guid.NewGuid()` を生成 + `user_sessions` INSERT を呼ぶ責務統合)
- JWT claim に `sid_session` (= session_id UUID) 追加
- `ICurrentUser.SessionId` プロパティ追加
- `IUserSessionStore` (DI 経由) で `CreateSessionAsync(userId, jwtJti) → sessionId` / `InvalidateSessionAsync(sessionId)` / `IsActiveAsync(sessionId)` 提供
- `JwtBearer` の token validation で `sid_session` が `user_sessions.deleted_at IS NULL` かを検証 (`OnTokenValidated` event)
- 既発行 token (claim 欠落) は invalid と判定して 401 を返す (← 互換性破壊)
- `AuthLoginTests` 既存テストの token assertion 拡張 (`sid_session` 存在確認)

### D201 GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png proxy (api/S)

API 内 `TilesEndpoints.cs` で Bearer JWT 検証後、GeoServer に basic auth で proxy。`HttpClient` 経由で WMS GetMap を組み立てる。

受け入れ条件:
- `api/Endpoints/TilesEndpoints.cs` 新規
- `GET /tiles/{layerId:int}/{theme}/{z:int}/{x:int}/{y:int}.png` 受領
- theme 名は `^[a-z0-9_]{1,32}$` で validation、不一致は 400
- GeoServer URL は `appsettings.json: GeoServer.BaseUrl` から構築 (`wms?service=WMS&request=GetMap&layers=l_{layerId}&styles=t_{theme}&...`)
- z/x/y → bbox 変換 (TileMatrix 計算) を `WebMercatorTileMath` クラスに切り出し
- 認可: admin/general/guest 全てアクセス可
- Bearer JWT 検証 → GeoServer に basic auth で叩く (basic auth 認証情報は `GeoServer.AdminUser` + 環境変数)
- HTTP `Cache-Control: max-age=3600, public` を返す
- GeoServer 接続失敗時 503

### D202 POST /api/selection + sid lifecycle (api/M)

`api/Endpoints/SelectionEndpoints.cs` 新規。`POST /api/selection { entityIds[], colorHex? } → 201 { sid }` で `selection_sets` レコード作成。`GET /tiles/selection/{sid}/{z}/{x}/{y}.png` で sid 検証 + GeoServer に `CQL_FILTER` 経由 proxy。`DELETE /api/selection/{sid}` でレコード削除。

受け入れ条件:
- `POST /api/selection` 受領: `user_id` = `ICurrentUser.UserId`、`session_id` = `ICurrentUser.SessionId`、`entity_ids` 配列で INSERT、`sid` を返す
- 認可: admin/general (guest 不可、403)
- `GET /tiles/selection/{sid}/...`: sid → `selection_sets` SELECT → user_id と current user 比較、不一致は 403
- entity_ids → CQL_FILTER 文字列を組み立て (`entity_id IN ('uuid1', 'uuid2', ...)`)
- GeoServer に basic auth で proxy
- `DELETE /api/selection/{sid}`: 発行者のみ、不一致は 403
- entity_ids 上限: 50,000 件 (それ超は 400 + ProblemDetails)
- CASCADE: `user_sessions.deleted_at` 埋め時に `selection_sets` が自動削除 (D102 0D04 で FK)

### D203 admin theme CRUD + GeoServer REST 同期 (api/M)

`api/Endpoints/AdminLayerStyleEndpoints.cs` 新規。`GET/PUT /api/admin/layers/{id}/style` で `layers.style_json` の CRUD。PUT 時に GeoServer REST API (`/geoserver/rest/styles/...`) に SLD POST する `IGeoServerStyleSync` を新設。

受け入れ条件:
- `GET /api/admin/layers/{id}/style` → 200 `{ themes: {...} }`
- `PUT /api/admin/layers/{id}/style` → 200、本文に themes JSON を受領、DB UPDATE + GeoServer REST POST
- 認可: admin のみ
- GeoServer REST 同期失敗時、DB rollback + 500 ProblemDetails
- `IGeoServerStyleSync.PushStyleAsync(layerId, themeName, sldXml)` インタフェース定義 + `GeoServerStyleSync` 実装
- DTO: `LayerStyleDto { themes: Dictionary<string, ThemeStyleDto> }`
- SLD JSON → SLD XML 変換は最小限 (色 / 線幅 / 透明度のみ Phase D で対応)
- `audit_log` 記録 (admin 操作 + actor_user_id 紐付け)

### D204 POST /api/auth/logout + selection_sets cascade 削除 (api/S)

`api/Endpoints/AuthEndpoints.cs` に `POST /api/auth/logout` 追加。`user_sessions.deleted_at` を埋め、関連 `selection_sets` が CASCADE 削除されることを確認。

受け入れ条件:
- `POST /api/auth/logout` 受領、Bearer JWT 必須
- `ICurrentUser.SessionId` → `user_sessions` UPDATE で `deleted_at = now()`
- `selection_sets` は FK CASCADE で自動削除
- 二重 logout (既に deleted_at 埋まり) は 204 No Content で冪等
- `audit_log` 記録 (logout 操作)
- 直後の同 JWT で API リクエスト → 401 (D103 の token validation で deleted_at 検知)

### D205 GET /api/features?layerId= Sunset ヘッダ + IApiClient.GetFeaturesAsync 削除 (api/S)

`GET /api/features?layerId=` endpoint に `Sunset: <future-date>` + `Deprecation: true` ヘッダを付ける。`windos-app/Services/IApiClient.GetFeaturesAsync` メソッドを削除 (Phase D で未使用化が確定したため)。WD3 末で 410 化する準備として、200 を返しつつヘッダで予告。

受け入れ条件:
- `?layerId=` のレスポンスに `Sunset: <Phase D 完了予定日>` + `Deprecation: true` ヘッダ
- レスポンス body は既存通り (Phase D' で 410 化、Phase D 内では 200 維持)
- `IApiClient.GetFeaturesAsync` メソッド削除 (Phase C smoke test の結果未使用と確認済)
- `ApiClient.cs` 実装も同時削除
- 既存 api.tests の `?layerId=` 使用箇所は WD5 D504 で書き換え (本 Wave では touch せず)
- WD3 D303 で API endpoint を 410 Gone に切り替える前提

### D301 WebGIS: VectorLayer → TileLayer 主役切替 + theme 切替 (webgis/M)

`webgis/src/map/mapInit.ts` で `VectorLayer` を削除し、`TileLayer({source: XYZ})` を主役に。URL は `${apiBase}/tiles/{layerId}/{theme}/{z}/{x}/{y}.png`、`tileLoadFunction` で Bearer JWT を Authorization ヘッダに付与。`wireLayerSelect` で theme パラメタを受け取り URL に組み込む。

受け入れ条件:
- `mapInit.ts` から `VectorSource` / `VectorLayer` 削除
- `baseTileLayer` (TileLayer) と `selectionOverlay` (空、D302 で source 設定) の 2 layer 構成
- URL: `${apiBase}/tiles/${layerId}/${theme}/{z}/{x}/{y}.png`
- `tileLoadFunction` で Authorization ヘッダ (Bearer JWT)
- `wireLayerSelect(layerId, theme)` で TileLayer の source を差替
- theme 切替: bridge `theme_change` envelope を受領、URL を `${theme}` 差替
- vitest 既存 green (新規 vitest は WD5 D502)

### D302 WebGIS: 選択 2 段パイプライン (webgis/M)

`webgis/src/controllers/selection.ts` を改修。OL `singleclick` でクリック位置に最寄りの entity_id を確定 (`tileLoadFunction` で `?clickX&clickY` を WMS GetFeatureInfo に投げる) → `POST /api/selection` で sid 取得 → `selectionOverlay.setSource(new XYZ({...selection url}))` で差替。

受け入れ条件:
- `selection.ts` のクリック処理: WMS GetFeatureInfo で entity_id 1 件取得 (D301 の TileLayer から)
- `POST /api/selection` を fetch で叩き sid 取得
- `selectionOverlay` の source を `XYZ({url: "/tiles/selection/{sid}/{z}/{x}/{y}.png"})` に差替
- WinForms に bridge `features_selected { entityIds, sid }` を送信
- 失敗時 (403 / 500) は OL Style 暫定ハイライト (赤枠) を維持しタイル差替なし
- D303 で envelope 拡張 + 多重選択 UI (ドラッグ矩形) を準備
- vitest 既存 green

### D303 WebGIS: bridge envelope 拡張 + ?layerId= を 410 化 (webgis/S)

`webgis/src/bridge/messages.ts` で `feature_clicked` envelope 削除 + `features_selected` / `theme_change` / `selection_overlay_ready` の 3 envelope 追加。API 側 `?layerId=` を 410 Gone に切り替える 1 行修正も含む (Phase D Sunset 期間が WD3 末で終了)。

受け入れ条件:
- `bridge/messages.ts` で envelope 定義差替
- `feature_clicked` envelope 完全削除
- `features_selected { entityIds[], sid }` 受信側 (WinForms) の対応は WD4
- `theme_change { layerId, theme }` 送信側 (WinForms → WebGIS) の対応は WD4
- `selection_overlay_ready { sid, count }` (WebGIS → WinForms)
- 同 PR で API `?layerId=` を 410 Gone に変更 (`FeatureEndpoints.cs` 1 行修正)
- `IApiClient` には `?layerId=` を呼ぶメソッドが残っていないことを再確認
- vitest 既存 green

### D401 WinForms: MainForm bridge handler + ApiClient メソッド差替 (winforms/M)

`MainForm.OnBridgeMessage` を更新: `feature_clicked` 削除、`features_selected` 配列受領、`selection_overlay_ready` 受領。`ApiClient` に `CreateSelectionAsync` / `DeleteSelectionAsync` / `UpdateLayerStyleAsync` / `GetLayerStyleAsync` / `LogoutAsync` を追加。theme 切替 ComboBox 追加。

受け入れ条件:
- `MainForm.cs` で `feature_clicked` ハンドラ削除
- `features_selected` ハンドラ追加 → 単数モード/N 件モードを `entityIds.Length` で分岐
- `theme_change` envelope 送信側追加 (theme ComboBox の SelectedIndexChanged で)
- `IApiClient` に新メソッド 5 件追加 + 実装
- theme ComboBox は `LayerSelectPayload.theme` を受け取った時に該当 layer の style_json.themes キー一覧を表示
- `dotnet test windos-app.tests -c Release` 既存 green (新規テストは WD5 D503)

### D402 WinForms: AttributeEditorControl N 件モード (winforms/S)

`AttributeEditorControl` に `LoadFeatures(IReadOnlyList<Guid> entityIds)` メソッド追加。N 件モード時は属性編集 UI を disable、件数とサンプル属性 (最大 5 件) を表示。1 件モードは現状維持。

受け入れ条件:
- `LoadFeatures(entityIds)` メソッド追加
- N 件モード UI: 件数表示 + 最初の 5 件の `feature_id` リスト + 「一括編集は Phase D' 対応予定」プレースホルダ
- 1 件モード時は既存 `LoadFeature(entityId)` を呼ぶ (互換)
- `saveButton.Enabled = false` (N 件編集は Phase D' 課題)
- guest UI 制限 (Phase A A404) は維持

### D501 api.tests 新規 4 件 (tests/M)

`Tests/Tiles/TilesProxyTests.cs` + `Tests/Selection/SelectionEndpointTests.cs` + `Tests/Admin/AdminLayerStyleTests.cs` + `Tests/Auth/AuthLogoutTests.cs` の 4 ファイル新規追加。WireMock.NET 等で GeoServer モック化。

受け入れ条件:
- `TilesProxyTests`: GeoServer モックに URL アサート + JWT 認可 (admin/general/guest) + Cache-Control ヘッダ
- `SelectionEndpointTests`: sid 発行 / 別ユーザ 403 / DELETE 冪等 / entity_ids 上限 50000 件 / CASCADE 削除
- `AdminLayerStyleTests`: style_json CRUD / admin role gate / GeoServer 同期失敗時の rollback
- `AuthLogoutTests`: logout → user_sessions.deleted_at + 同 token で 401 + 二重 logout 冪等
- 全テスト green、既存 60 件 + 新規約 30 件 = 計約 90 件
- WireMock.NET を `api.tests.csproj` に追加 (CI 影響確認)

### D502 webgis vitest 新規 2 件 (tests/S)

`webgis/test/map/tileLayer.test.ts` + `webgis/test/controllers/selection.test.ts` の 2 ファイル新規追加。

受け入れ条件:
- `tileLayer.test.ts`: URL 構築 (layerId / theme / z / x / y 引数) + Authorization ヘッダ付与
- `selection.test.ts`: クリック → POST → overlay source 差替シーケンス + 403 時のフォールバック
- 既存 vitest 全 green
- カバレッジ計算は別 Issue (Phase D' 候補)

### D503 windos-app.tests 新規 2 件 (tests/S)

`Tests/Forms/MainFormThemeChangeTests.cs` + `Tests/Controls/AttributeEditorMultiModeTests.cs` の 2 ファイル新規追加。

受け入れ条件:
- `MainFormThemeChangeTests`: ComboBox SelectedIndexChanged → bridge `theme_change` envelope 発火 (FakeBridge で確認)
- `AttributeEditorMultiModeTests`: N 件モードで saveButton.Enabled=false + 件数表示
- 全テスト green、既存 118 件 + 新規 2 件 = 計 120 件
- Release 構成で実行 (`smart_app_control_pitfall` メモリ準拠)

### D504 ?layerId= 依存テスト書き換え + 50 万件性能 smoke (tests/M)

WD0 D100 で計上した影響範囲のテストを `{entityId}` ループ or DB SELECT に書き換え。50 万件 fixture (Phase C `sample-shp-generator` を 1000 倍化) で `GET /tiles/...` の応答時間 smoke。

受け入れ条件:
- WD0 で計上した影響テスト全件書き換え (推定 10-20 件)
- 50 万件 fixture 生成スクリプト `tools/perf/generate-500k.sh` 追加
- 性能 smoke スクリプト `tools/perf/tile-smoke.sh` (curl × 5 で z=15 タイル平均応答時間)
- z=15 平均応答時間 < 500ms 確認 (実測値を `PHASE_D_INDEX.md` 記録)
- > 2s の場合 GeoServer index 設計を見直すブロッカー判定
- 全 api.tests / vitest / windos-app.tests green

### D601 docs/rendering.md (docs/S)

Phase D アーキ解説。データフロー図 + theme 仕組み + selection 2 段パイプライン + tile cache 戦略 + Phase D' 申し送り。

受け入れ条件:
- `docs/rendering.md` 新規作成 (約 300-500 行)
- データフロー 4 系統 (通常表示 / theme 切替 / 選択 / 編集) を図示
- SLD パターン集 5 例 (single color / categorical / numerical / outline / icon)
- Phase D' 申し送り (MapProxy / カスタム theme UI / batch-update / 編集→無効化最適化)
- メモリ 3 本 (`scale_target_and_server_side_rendering` / `selection_visualization_and_multi_select` / `rendering_architecture_shift`) からの抜粋 + 反映

### D602 docs/deploy/geoserver-prod.md + PHASE_D_INDEX.md 最終化 (docs/S)

本番別ホスト構成の手順 + `docs/PHASE_D_INDEX.md` を Wave PR 一覧 + 受け入れ条件チェックリストで最終化。

受け入れ条件:
- `docs/deploy/geoserver-prod.md` 新規 (約 200 行)
  - k8s manifest 例 (Deployment + Service + PVC)
  - VM 直構成例 (systemd unit)
  - PostgreSQL JNDI 接続情報
  - SSL/TLS 設定 (reverse proxy 経由 or 直接)
  - JWT pass-through の認証経路
  - 本番 GeoServer のセットアップ手順 (admin password 変更等)
- `docs/PHASE_D_INDEX.md` 最終化 (Wave PR 一覧 + 受け入れ条件全項目チェックリスト + Phase D 完了報告)
- `MEMORY.md` を Phase D 完了状態に更新 (orchestration_state.md 連動)
