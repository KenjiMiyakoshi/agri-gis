# agri-gis

PostGIS をバックエンドに据えた**汎用 WebGIS 基盤**。
バイテンポラル（履歴）+ 監査ログ + ユーザー定義スキーマを中核要件とし、
WinForms クライアント + WebView2 + OpenLayers + ASP.NET Core Web API のハイブリッド構成。

> ディレクトリ名は `agri-gis` ですが、内容はドメイン非依存の汎用 GIS 基盤です。
> サンプルデータの「圃場」「観測点」は表示確認用のフィクスチャに過ぎません。

```
agri-gis/
├── docker-compose.yml                 PostGIS + pgAdmin
├── AgriGis.sln                        ソリューションファイル
├── db/
│   ├── init/                          初回起動時に自動実行 (docker-entrypoint-initdb.d)
│   │   ├── 001_init.sql               layers / feature_current の素体
│   │   └── 002_seed.sql               表示確認用シード (新スキーマ前提)
│   └── migration/                     差分マイグレーション (手動適用)
│       ├── 001_layers_add_schema.sql  schema_json / schema_version
│       ├── 002_feature_current_audit.sql 監査列 + TIMESTAMPTZ 化
│       ├── 003_feature_history.sql    履歴テーブル
│       ├── 004_audit_log.sql          監査ログ
│       ├── 005_layer_schema_version.sql append-only スキーマ履歴
│       ├── 006_fn_feature_insert.sql  PL/pgSQL 関数
│       ├── 007_fn_feature_update.sql  楽観ロック付き更新
│       ├── 008_fn_feature_delete.sql  履歴退避 + 物理削除
│       └── 009_fn_layer_schema_upsert.sql スキーマ更新
├── api/                               ASP.NET Core Web API (.NET 8 Minimal API)
│   ├── Endpoints/                     MapGroup 3 分割 (layers / features / admin)
│   ├── Dto/                           record DTO 一式
│   ├── Middleware/                    RequestContext / ProblemDetails
│   ├── Errors/                        ApiException 系
│   └── Validation/                    AttributeValidator
├── api.tests/                         xUnit + Testcontainers (実 PostGIS)
├── webgis/                            OpenLayers + TypeScript (Vite)
│   └── src/
│       ├── map/                       Map/View/Layer
│       ├── api/                       client + types (API DTO ミラー)
│       ├── bridge/                    WebView2 ↔ WebGIS envelope
│       └── controllers/               layer / selection / rotation
├── windos-app/                        WinForms クライアント (.NET 8 + WebView2)
│   ├── Core/                          純粋ロジック (System.Windows.Forms 不参照)
│   ├── Services/                      ApiClient / BridgeMessenger
│   ├── Dto/                           API DTO の C# ミラー
│   └── Forms/                         MainForm + AttributeEditorControl
├── windos-app.tests/                  Core + ConventionTest
└── docs/
    ├── issues/                        40 のサブイシュー + MAPPING.md
    ├── testing-policy.md              テスト方針
    └── message-protocol.md            WebView2 ↔ WebGIS 通信規約
```

## 設計の柱

| 柱 | 概要 |
|---|---|
| **バイテンポラル** | `feature_current` と `feature_history` に分け、`valid_from` / `valid_to` (DATE 粒度) で過去断面を保持 |
| **監査ログ** | 全書き込みを `audit_log` に独立記録（`actor` / `action` / `before_doc` / `after_doc` / `request_id`） |
| **楽観ロック** | `PATCH /api/features/{entityId}` は `If-Match: <version>` ヘッダ必須。不一致は 409 |
| **スキーマバージョニング** | `layer_schema_version` で append-only に履歴。旧バージョンは `valid_to` で閉じる |
| **ユーザー定義スキーマ** | `layers.schema_json` (JSONB) でレイヤごとに属性定義。API/UI で動的に解釈 |
| **書き込みはPL/pgSQL関数経由** | `fn_feature_insert/update/delete` + `fn_layer_schema_upsert` で履歴退避と監査追記をアトミック化 |

詳細：[`docs/issues/README.md`](docs/issues/README.md) と [`docs/issues/MAPPING.md`](docs/issues/MAPPING.md)

## 必要なもの

| ツール | バージョン |
| --- | --- |
| Docker Desktop | 任意 (PostGIS + Testcontainers で使用) |
| .NET SDK | 8.0+ |
| Node.js | 20+ |
| Visual Studio Code or 2022 | 任意 (WinForms の Designer は VS 2022 推奨) |
| WebView2 Runtime | Edge Chromium 同梱 (Win11/10 標準) |

## 起動手順

### 1. PostGIS / pgAdmin を起動

```powershell
docker compose up -d
```

- PostGIS: `localhost:5432` (DB: `agri_gis` / user: `agri_user` / pass: `agri_pass`)
- pgAdmin: <http://localhost:8081> (Email: `admin@example.com` / pass: `admin_pass`)

`db/init/*.sql` はコンテナ初回起動時のみ自動実行されます。

### 2. マイグレーションを適用

`db/migration/` の SQL 群は手動適用が前提（運用ルールは [`db/migration/README.md`](db/migration/README.md)）。
PowerShell で連続適用：

```powershell
Get-ChildItem db/migration/*.sql | Sort-Object Name | ForEach-Object {
  Write-Host "==> $($_.Name)"
  Get-Content $_.FullName -Raw | docker exec -i agri_postgis psql -U agri_user -d agri_gis -v ON_ERROR_STOP=1
}
```

> 既存環境のシードが旧スキーマの場合は、migration 適用後に手動で `002_seed.sql` を流し直す必要があります（`docker compose down -v` で作り直すのが一番素直）。

### 3. API を起動

```powershell
cd api
dotnet run
```

`http://localhost:5080` で待ち受けます。接続文字列は環境変数 `AGRI_GIS_DB` または `appsettings.json` の `ConnectionStrings:AgriGis` で上書き可。

動作確認：

```powershell
curl http://localhost:5080/api/health
curl http://localhost:5080/api/layers
curl "http://localhost:5080/api/features?layerId=1"
```

### 4. WebGIS を起動

```powershell
cd webgis
npm install
npm run dev
```

<http://localhost:5173> を開く。Vite dev server が `/api/*` を `5080` にプロキシします。

### 5. WinForms クライアントを起動

```powershell
cd windos-app
dotnet run
```

別ウィンドウで `AgriGis` フォームが立ち上がり、WebView2 で WebGIS が埋め込まれます。
**起動前に API (5080) と WebGIS (5173) が起動していること**。

## エンドポイント仕様

### 共通仕様

- ベース URL: `http://localhost:5080`
- すべてのレスポンス JSON は **camelCase**
- 書き込み系（`POST` / `PATCH` / `PUT` / `DELETE`）は **`X-Actor` ヘッダ必須**（未指定で 400）
- `X-Request-Id` ヘッダ任意。未指定はサーバが採番し、レスポンスにも `X-Request-Id` で返す。`audit_log.request_id` と同期
- エラー応答は `ProblemDetails` (`application/problem+json` 相当)。属性別エラーは `extensions.errors[]` または top-level `errors[]` に `{ attributeKey, code, message }` 形式で列挙

### エンドポイント一覧

| メソッド | パス | 必須ヘッダ | 概要 |
|---|---|---|---|
| GET | `/api/health` | - | ヘルスチェック → `{"status":"ok"}` |
| GET | `/api/layers` | - | レイヤ一覧（`schema_json` 含む） |
| GET | `/api/layers/{layerId:int}/schema` | - | 個別レイヤのスキーマ |
| PUT | `/api/admin/layers/{layerId:int}/schema` | X-Actor | スキーマ更新 (`fn_layer_schema_upsert`) |
| GET | `/api/features?layerId=&asOf=YYYY-MM-DD` | - | フィーチャ一覧。`asOf` 省略時は現行のみ、指定時は履歴を UNION ALL |
| GET | `/api/features/{entityId:guid}?asOf=YYYY-MM-DD` | - | 個別フィーチャ取得（0 件で 404） |
| GET | `/api/features/{entityId:guid}/history` | - | 履歴一覧 (`valid_to DESC`) |
| POST | `/api/features` | X-Actor | 新規作成 (`fn_feature_insert`) → 201 + Location |
| PATCH | `/api/features/{entityId:guid}` | X-Actor + **If-Match** | 楽観ロック付き更新 (`fn_feature_update`) |
| DELETE | `/api/features/{entityId:guid}` | X-Actor | 論理削除（履歴退避）→ 204 |

### ステータスコードマップ

| HTTP | 原因 |
|---|---|
| 400 | `X-Actor` 欠落、`asOf` の形式違反 (ISO datetime 等) |
| 404 | 対象 entityId / layerId が不存在 |
| 409 | `If-Match` の version が現行と不一致 (PostgreSQL `40001`) |
| 422 | 属性スキーマ違反 (`errors[]` 必須欠落 / 型不一致) |
| 428 | `PATCH` で `If-Match` 欠落 |

詳細は [`docs/message-protocol.md`](docs/message-protocol.md) と [`docs/testing-policy.md`](docs/testing-policy.md)。

## 既知の制約

- **認証なし**：`X-Actor` はクライアントが申告する文字列を信頼する。本格認証導入は次サイクル
- **図形編集 UI なし**：API は `PATCH /api/features/{id}` で `geometry` を受け取れるが、WebGIS の Draw/Modify UI は未実装
- **マイグレーションは手動適用**：Flyway 等のツールは未導入。`docker compose down -v` で再構築するのが現状の運用
- **WebView2 専用**：WebGIS は WebView2 ホスト下で動かす前提。ブラウザ単独起動も dev では動くが、bridge 通信は機能しない

## トラブルシュート

- **API が DB に繋がらない**: `docker compose ps` で `agri_postgis` が `Up` か確認、`5432` 衝突をチェック。接続文字列を `$env:AGRI_GIS_DB` で明示
- **WebGIS に何も表示されない**: DevTools で `/api/layers` のレスポンスを確認。`schema_json` 列が無いエラーが出ていればマイグレーション未適用
- **WinForms 起動でハング**: API (5080) と WebGIS (5173) が起動しているか確認。WebView2 Runtime が古い場合は Edge の更新を
- **保存で 409 が出る**: 他のクライアントが先に保存している。WebGIS のレイヤ再読込で最新版を取得し、編集をやり直す

## テスト

```powershell
# API 結合 (要 Docker)
dotnet test api.tests

# WinForms Core 単体 + ConventionTest
dotnet test windos-app.tests

# WebGIS (envelope / requestId 重複検知)
cd webgis; npm run test
```

方針詳細：[`docs/testing-policy.md`](docs/testing-policy.md)
