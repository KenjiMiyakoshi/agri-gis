# Phase D Design 案 P — 採択案 (案 A ベース + Plan 工程ユーザー判断 4 件の反映)

`PHASE_D_DESIGN_A.md` (GeoServer 同梱 + MapProxy) をベースに、`PHASE_D_PLAN.md` §3.2 のユーザー判断 4 件を反映した最終 Design。Phase D Issue 化フェーズの直接入力。

## 1. 採用ベースと選択理由

**ベース案 = 案 A (GeoServer 同梱)**

選択理由:
- メモリ `scale_target_and_server_side_rendering.md` が既に本命指定 (Plan 工程と整合)
- 数百万件 + 選択 raster overlay の両要件を「枯れた WMS パターン」で吸収
- OSS GIS のデファクト、人材豊富、SLD 学習リソース潤沢
- 案 B (MapServer) は動的 theme 切替とコミュニティが弱い、案 C (自前) は工数 2-3 倍
- ライセンス GPL はプロセス分離 (Docker 別コンテナ + 本番別ホスト) で影響軽微

## 2. ユーザー判断 4 件の反映 (Plan §3.2)

### 2.1 GeoServer 配置: dev=Compose / 本番=別ホスト

`docker-compose.yml` (dev) のみ geoserver サービスを追加。本番は別ホスト前提のため:

- WD1 範囲: `docker-compose.yml` 拡張 + `geoserver/data_dir/` 初期セット
- 本番別ホスト構成 (k8s manifest / VM provisioning) は **WD5 で `docs/deploy/geoserver-prod.md` として別途**
- API → GeoServer の通信は dev (Docker network: `geoserver:8080`) / 本番 (`https://geoserver.internal/`) で URL 切替 = `appsettings.json: GeoServer { BaseUrl, AdminUser, AdminPasswordEnv }`
- 本番 GeoServer のセットアップは Phase D 範囲外、Phase D' 課題候補

### 2.2 `?layerId=` Sunset 期間: WD3 完了時点で即 410

並走期間なし、WD3 で WebGIS 切替完了と同時に API 側も 410:

- WD2 (API): `GET /api/features?layerId=` に `Sunset: <date>` + `Deprecation: true` ヘッダを付ける
- WD3 (WebGIS): `vectorSource.addFeatures` 削除、`TileLayer` 専用に切替
- WD3 末: API 側 `?layerId=` を **410 Gone** に変更 (Endpoint は残し 410 を返す)
- WD5: 410 Gone を `[Obsolete]` 維持 + テスト書き換え (+1.5d)

### 2.3 SLD 保管場所: DB `layers.style_json` JSONB を初期から

- WD1 (DB migration): `0D01_layers_style_json.sql` で `layers.style_json JSONB NOT NULL DEFAULT '{}'` 追加
- WD1 (GeoServer): 初期 `default.sld` を `geoserver/data_dir/styles/` に置く + `layers.style_json` のデフォルト値も初期 SLD の JSON 表現に
- WD2 (API): `GET /api/admin/layers/{id}/style`、`PUT /api/admin/layers/{id}/style` 追加。PUT 時に GeoServer REST API (`/geoserver/rest/styles/...`) に SLD POST する `IGeoServerStyleSync` を新設
- WD2: PUT で GeoServer 同期失敗時のロールバック方針 = DB 書き込み rollback (transaction 内)
- カスタム theme 編集 Web UI は Phase D' 申し送り

### 2.4 選択 sid TTL: セッション終了まで + 発行ユーザのみ取得可能

- WD1 (DB migration): `0D02_selection_sets.sql` + `0D03_user_sessions.sql` 追加 (`user_sessions` は JWT lifecycle 管理用テーブル新設)
- WD1 (API auth): JWT 発行時に `user_sessions` レコード INSERT、`session_id` を JWT claim `sid_session` に積む (既存 JWT secret 鍵を再利用、新規 claim 1 つ)
- WD2 (API): `POST /api/selection` で `selection_sets` レコード作成、`user_id` は `ICurrentUser.UserId`
- WD2 (API): `GET /tiles/selection/{sid}/...` で sid → user_id を取得 → 現在のユーザと一致しなければ 403
- WD2 (logout): `POST /api/auth/logout` (Phase A 拡張) で `user_sessions.deleted_at` を埋め、`selection_sets` を CASCADE 削除

## 3. 未確定論点 (Phase D 中に決定)

| # | 論点 | 着手 Wave |
|---|---|---|
| Q1 | theme 切替操作の role 制限 (guest 可? general 以上?) | WD2 (API endpoint 設計時) |
| Q2 | カスタム theme 削除 (`DELETE /api/admin/layers/{id}/style/{theme}`) を Phase D に含めるか | WD2 |
| Q3 | layer 削除時の関連 `selection_sets` カスケード扱い | WD2 (DB constraint) |
| Q4 | tile 認可エラー時の placeholder PNG (空 PNG) を返すか 403 を返すか | WD2 (UX 判断) |
| Q5 | logout 時の `selection_sets` 即削除 vs lazy GC (deleted_at flag + 夜間 batch) | WD1 (sid lifecycle 全体設計時) |

Q1-Q5 は Wave 実装中に PR description で論点提示 + 1 オプション仮採用 + ユーザーレビューで確定する流儀 (Phase B/C と同じ)。

## 4. Phase D で追加する DB マイグレーション (4 本)

| # | ファイル | 内容 |
|---|---|---|
| 0D01 | `0D01_layers_style_json.sql` | `layers.style_json JSONB NOT NULL DEFAULT '{}'` |
| 0D02 | `0D02_user_sessions.sql` | `user_sessions(session_id UUID PK, user_id UUID FK, jwt_jti TEXT UNIQUE, created_at TIMESTAMPTZ, deleted_at TIMESTAMPTZ)` + 既存 JWT 発行/検証経路の修正 |
| 0D03 | `0D03_selection_sets.sql` | `selection_sets(sid UUID PK, user_id UUID FK CASCADE, entity_ids UUID[] NOT NULL, color_hex TEXT DEFAULT '#FFEB3B', created_at TIMESTAMPTZ)` + GIST index は省略 (CQL_FILTER 経由のため) |
| 0D04 | `0D04_selection_sets_session_link.sql` | `selection_sets.session_id UUID NULL REFERENCES user_sessions(session_id) ON DELETE CASCADE` + ALTER で `user_id` の CASCADE を session_id 経由に変更 |

0D04 は 0D03 と統合してもよいが、Phase A/B/C の流儀に揃え「1 機能 = 1 migration」で分割。

## 5. Phase D で追加する API endpoint (8 本)

| Method | Path | 認可 | Issue |
|---|---|---|---|
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` | Bearer (admin/general/guest) | D201 |
| POST | `/api/selection` | Bearer (admin/general) | D202 |
| GET | `/tiles/selection/{sid}/{z}/{x}/{y}.png` | Bearer + sid owner | D202 |
| DELETE | `/api/selection/{sid}` | Bearer + sid owner | D202 |
| GET | `/api/admin/layers/{id}/style` | Bearer admin | D203 |
| PUT | `/api/admin/layers/{id}/style` | Bearer admin | D203 |
| POST | `/api/auth/logout` | Bearer | D204 |
| (GET | `/api/features?layerId=` | -) | **WD3 末で 410 / D205** |

詳細は `PHASE_D_ISSUES_INDEX.md` の D2xx 一覧。

## 6. WebGIS 変更 (4 ファイル)

| ファイル | 変更概要 | Issue |
|---|---|---|
| `webgis/src/map/mapInit.ts` | `VectorLayer` 削除、`baseTileLayer` + `selectionOverlay` の 2 TileLayer 構成 | D301 |
| `webgis/src/controllers/selection.ts` | クリック → `POST /api/selection` → overlay 差替の 2 段パイプライン | D302 |
| `webgis/src/controllers/layer.ts` | `wireLayerSelect` の URL 構築を tile URL に。theme パラメタを受け取る | D301 |
| `webgis/src/bridge/messages.ts` | `feature_clicked` 廃止、`features_selected` / `theme_change` / `selection_overlay_ready` の 3 envelope 追加 | D303 |

## 7. WinForms 変更 (3 ファイル)

| ファイル | 変更概要 | Issue |
|---|---|---|
| `windos-app/Forms/MainForm.cs` | `OnBridgeMessage` で `feature_clicked` 削除 → `features_selected` 配列受領、`theme_change` 送信。`AttributeEditor` を単数/N 件モード切替 | D401 |
| `windos-app/Services/ApiClient.cs` | `GetFeaturesAsync` 削除、`CreateSelectionAsync(entityIds[]) → sid`、`DeleteSelectionAsync(sid)`、`UpdateLayerStyleAsync(layerId, styleJson)`、`GetLayerStyleAsync(layerId)`、`LogoutAsync()` 追加 | D401 |
| `windos-app/Controls/AttributeEditorControl.cs` | `LoadFeatures(entityIds[])` 追加、N 件モード時は属性編集 disable | D402 |

## 8. テスト書き換え方針 (`?layerId=` 依存)

`grep -r "GET /api/features?layerId" api.tests/` で影響範囲を WD0 で計上、WD5 で集中対応。

### 8.1 想定影響 (実測は WD0 で確定)

- `InsertInvariantTests`: feature INSERT 後の件数確認 → `SELECT COUNT(*) FROM feature_current WHERE layer_id=N` に置換
- `UpdateInvariantTests`: feature UPDATE 前後の状態確認 → 単一 `GET /api/features/{entityId}` に置換
- `DeleteInvariantTests`: feature DELETE 後の不可視確認 → 単一 GET の 404 確認に置換
- `AuthorizationTests`: `GET /api/features` の role 別 → 新規 `GET /tiles/...` の role 別に書き換え

### 8.2 書き換え戦略

- DB 直接 SELECT は `DbTestHarness` (Phase A 既存 fixture) を経由
- 単一 GET ループは benchmark 影響あり (10000 件で 10000 リクエスト)、ループ件数を 100 件にサンプリング

## 9. テスト戦略

### 9.1 新規テスト

| カテゴリ | テスト | Issue |
|---|---|---|
| API | `TilesProxyTests` (WireMock.NET で GeoServer モック、URL 構築 / JWT validation) | D501 |
| API | `SelectionEndpointTests` (sid 発行 / 別ユーザ 403 / sid TTL / CASCADE 削除) | D501 |
| API | `AdminLayerStyleTests` (style_json CRUD + admin role gate + GeoServer 同期失敗ロールバック) | D501 |
| API | `AuthLogoutTests` (logout → user_sessions.deleted_at + selection_sets CASCADE) | D501 |
| WebGIS | `tileLayer.test.ts` (URL 構築 + JWT injection) | D502 |
| WebGIS | `selection.test.ts` (POST → overlay 差替シーケンス) | D502 |
| WinForms | `MainFormThemeChangeTests` (bridge 発火) | D503 |
| WinForms | `AttributeEditorMultiModeTests` (N 件 disable) | D503 |
| 性能 | 50 万件レイヤ × z=15 × 5 リクエスト平均 < 500ms | D504 |

### 9.2 書き換えテスト

| カテゴリ | 既存 → 新規 | Issue |
|---|---|---|
| API | `?layerId=` 経路を使う Invariant 系を `{entityId}` ループ or DB SELECT に | D504 |
| WebGIS | `vectorSource.addFeatures` 経路の vitest を TileLayer 経路に | D502 (一部) |

## 10. 案 A からの変更点 (Plan ユーザー判断の反映による)

| 案 A | 案 P (本案) |
|---|---|
| `selection_sets.user_id` 単独 FK | `selection_sets.session_id` FK + 0D02 で `user_sessions` 新設 |
| 案 A は sid TTL を仮 "session 終了まで" のみ言及 | 0D04 で `selection_sets.session_id CASCADE` を明示化、logout endpoint も新設 (D204) |
| 案 A は SLD 配信を「.sld 直置き or DB」両論併記 | DB `layers.style_json` を初期採用と確定 (WD1 D102 で migration、WD2 D203 で API) |
| 案 A はテスト書き換え工数を WD5 で +1.5d 見積 | 同上維持。WD0 PoC 中に grep 計上 |
| 案 A は theme 切替 role 未決 | 未決のまま (Q1)、WD2 で仮確定 |

## 11. リスク (案 P 残存)

| # | リスク | 対応 |
|---|---|---|
| P1 | DB migration 4 本の rollback 順序 | `0D04 → 0D03 → 0D02 → 0D01` の逆順で down script を WD1 D101 に含める |
| P2 | JWT に session_id claim 追加で既存 token 互換性が壊れる | 既発行 token を全て無効化 (Phase D デプロイ時に全ユーザ再ログイン)。`docs/deploy/geoserver-prod.md` に明記 |
| P3 | GeoServer の data_dir bind mount が CI 環境で動かない | api.tests は GeoServer を起動せず HttpClient モックのみ。CI は docker-compose を起動しない (R2 と統合) |
| P4 | tile URL の hash collision (theme 名のサニタイズ) | theme 名は `[a-z0-9_]{1,32}` の正規表現で validation (WD2 D203 で実装) |
| P5 | 数百万件 × N theme × N tile のキャッシュサイズ | GeoServer 内部キャッシュは TTL 1h で自動 GC。MapProxy 永続キャッシュは Phase D' |
| P6 | 編集 → タイル無効化の遅延 (楽観的更新) | Phase D は手動リロード前提。Phase D' で WebSocket 通知検討 |

## 12. 受け入れ条件 (Phase D 完了の定義)

1. dev `docker-compose up -d` で geoserver サービスが起動、`/geoserver/web/` が HTTP 200
2. WebGIS で `?layerId=` 経由のベクタ読み込みが 410 Gone、代わりに TileLayer で図形が表示される
3. クリック選択 → `POST /api/selection` → 選択 overlay TileLayer が表示される (2 段パイプライン動作確認)
4. 50 万件 fixture で z=15 タイル平均応答時間 < 500ms
5. `windos-app.tests` + `api.tests` + `webgis vitest` 全 green
6. `docs/rendering.md` + `docs/deploy/geoserver-prod.md` 作成
7. PR 単位で全 6 Wave (WD0-WD5) が main にマージ済
8. `orchestration_state.md` メモリ更新

## 13. 関連ドキュメント

- `PHASE_D_PLAN.md`: Plan 工程
- `PHASE_D_DESIGN_A.md`: 採用ベース案
- `PHASE_D_DESIGN_B.md`: 落選案 B (MapServer)
- `PHASE_D_DESIGN_C.md`: 落選案 C (自前 SkiaSharp)
- `PHASE_D_WAVE_PLAN.md`: Wave 分割詳細
- `PHASE_D_ISSUES_INDEX.md`: Issue 一覧 + 各 Issue 詳細
- `docs/PHASE_D_INDEX.md`: 高位サマリ
