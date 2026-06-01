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
│       ├── 009_fn_layer_schema_upsert.sql スキーマ更新
│       ├── 0A01_auth_core_tables.sql   Phase A: organizations / users / user_roles
│       ├── 0A02_audit_log_actor_user_id.sql actor_user_id 列追加 (FK users)
│       ├── 0A03_feature_current_entity_unique.sql entity_id UNIQUE 補強
│       ├── 0A04_fn_feature_update_delete_c1.sql C1 修復 (valid_from/to 接合)
│       ├── 0A05_fn_audit_geom_strip_c2.sql C2 修復 (audit から geom 除外)
│       └── 0A06_fn_args_extension.sql  関数引数に user_id/org_id 追加 + NOT NULL 化
├── api/                               ASP.NET Core Web API (.NET 8 Minimal API)
│   ├── Endpoints/                     MapGroup (auth / layers / features / admin)
│   ├── Auth/                          JwtService / PasswordHasher / ICurrentUser /
│   │                                  InitialAdminBootstrap / 401 ProblemDetails handler
│   ├── Dto/                           record DTO 一式 (Auth / Admin Org/User 含む)
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
│   ├── Auth/                          ISessionStore / Session (in-memory)
│   ├── Services/                      ApiClient / BridgeMessenger / BearerHandler /
│   │                                  UnauthorizedApiException
│   ├── Dto/                           API DTO の C# ミラー (Auth 含む)
│   └── Forms/                         MainForm + AttributeEditorControl + LoginForm
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

**Phase A 以降**：起動前に JWT 署名鍵と初期 admin パスワードを環境変数で設定する。

```powershell
$env:AGRI_GIS_JWT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$env:AGRI_GIS_INITIAL_ADMIN_PW = "ChangeMe-StrongPw123!"
cd api
dotnet run
```

`http://localhost:5080` で待ち受けます。接続文字列は環境変数 `AGRI_GIS_DB` または `appsettings.json` の `ConnectionStrings:AgriGis` で上書き可。

主要な環境変数:

| 環境変数 | 必須 | 用途 |
|----------|:----:|------|
| `AGRI_GIS_JWT_SECRET` | yes | HS256 署名鍵 (32+ bytes)。未設定で fail-fast |
| `AGRI_GIS_INITIAL_ADMIN_PW` | yes | 初期 admin パスワード。`InitialAdminBootstrap` が起動時に upsert |
| `AGRI_GIS_JWT_ISSUER` / `AGRI_GIS_JWT_AUDIENCE` | no | 既定 `agri-gis-api` / `agri-gis-windows` |
| `AGRI_GIS_JWT_TTL_HOURS` | no | 既定 8 |
| `AGRI_GIS_SKIP_BOOTSTRAP` | no | `1` で初期 admin upsert を抑制 (テスト用) |
| `AGRI_GIS_DB` | no | 接続文字列上書き |

動作確認：

```powershell
# health は anonymous で OK
curl http://localhost:5080/api/health

# login → JWT 取得 → /api/layers
$tok = (Invoke-RestMethod -Uri http://localhost:5080/api/auth/login -Method POST `
  -ContentType 'application/json' `
  -Body (@{ loginId='admin'; password=$env:AGRI_GIS_INITIAL_ADMIN_PW } | ConvertTo-Json)).accessToken
Invoke-RestMethod -Uri http://localhost:5080/api/layers -Headers @{ Authorization = "Bearer $tok" }
```

認証・認可の詳細は [`docs/auth.md`](docs/auth.md)。

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

起動するとまず `LoginForm` が表示される（ログイン ID とパスワード）。
初回は `admin` / `$env:AGRI_GIS_INITIAL_ADMIN_PW` で入る。
ログイン成功後に `MainForm` が立ち上がり、WebView2 で WebGIS が埋め込まれる。
**起動前に API (5080) と WebGIS (5173) が起動していること**。

- guest ロールでログインすると、属性エディタの「保存」ボタンが無効化される。
- access token (8h) 期限切れ／改ざん時は API が 401 を返し、自動的に再ログイン画面が出る。

## エンドポイント仕様

### 共通仕様

- ベース URL: `http://localhost:5080`
- すべてのレスポンス JSON は **camelCase**
- **Phase A 以降**: `/api/health` / `/api/auth/login` 以外は **`Authorization: Bearer <JWT>` ヘッダ必須**（未指定で 401）。`X-Actor` ヘッダは廃止
- 認可ポリシー: 書き込み系 (`POST/PATCH/DELETE /api/features`) は admin/general、`/api/admin/*` は admin のみ。詳細は [`docs/auth.md`](docs/auth.md) のロールマトリクス
- `X-Request-Id` ヘッダ任意。未指定はサーバが採番し、レスポンスにも `X-Request-Id` で返す。`audit_log.request_id` と同期
- エラー応答は `ProblemDetails` (`application/problem+json` 相当)。属性別エラーは `extensions.errors[]` または top-level `errors[]` に `{ attributeKey, code, message }` 形式で列挙

### エンドポイント一覧

| メソッド | パス | 認可 | 概要 |
|---|---|---|---|
| GET | `/api/health` | anonymous | ヘルスチェック → `{"status":"ok"}` |
| POST | `/api/auth/login` | anonymous | login_id + password → `{accessToken, expiresAt, user}` |
| GET | `/api/auth/me` | authenticated | claims から自分のユーザ情報 |
| POST | `/api/auth/change-password` | authenticated | 自パスワード変更 |
| GET | `/api/layers` | authenticated | レイヤ一覧（`schema_json` 含む） |
| GET | `/api/layers/{layerId:int}/schema` | authenticated | 個別レイヤのスキーマ |
| PUT | `/api/admin/layers/{layerId:int}/schema` | role: admin | スキーマ更新 (`fn_layer_schema_upsert`) |
| `*` | `/api/admin/organizations` | role: admin | 組織 CRUD（論理削除） |
| `*` | `/api/admin/users` | role: admin | ユーザ CRUD + `PUT /{id}/password` でリセット |
| GET | `/api/features?layerId=&asOf=YYYY-MM-DD` | authenticated | フィーチャ一覧。`asOf` 省略時は現行のみ |
| GET | `/api/features/{entityId:guid}?asOf=YYYY-MM-DD` | authenticated | 個別フィーチャ取得（0 件で 404） |
| GET | `/api/features/{entityId:guid}/history` | authenticated | 履歴一覧 (`valid_to DESC`) |
| POST | `/api/features` | role: admin/general | 新規作成 (`fn_feature_insert`) → 201 + Location |
| PATCH | `/api/features/{entityId:guid}` | role: admin/general + **If-Match** | 楽観ロック付き更新 (`fn_feature_update`) |
| DELETE | `/api/features/{entityId:guid}` | role: admin/general | 論理削除（履歴退避）→ 204 |

### ステータスコードマップ

| HTTP | 原因 |
|---|---|
| 400 | `asOf` の形式違反 (ISO datetime 等) |
| 401 | `Authorization: Bearer` 欠落／署名違反／期限切れ／login_id 不正 |
| 403 | 認証済みだがロール権限不足 (例: guest が POST /api/features) |
| 404 | 対象 entityId / layerId が不存在 |
| 409 | `If-Match` の version が現行と不一致 (PostgreSQL `40001`) |
| 422 | 属性スキーマ違反 (`errors[]` 必須欠落 / 型不一致) |
| 428 | `PATCH` で `If-Match` 欠落 |

詳細は [`docs/message-protocol.md`](docs/message-protocol.md) と [`docs/testing-policy.md`](docs/testing-policy.md)。

## 既知の制約

- **refresh token なし**：Phase A は access 8h のみ。期限切れで再ログイン (`UnauthorizedApiException` → `LoginForm` の自動表示で UX 緩和)
- **WebGIS は JWT 非保持**：CORS Origin 限定 (`localhost:5173`) のみで保護。トークン引き渡しは Phase B
- **テナント分離なし**：`org_id` は claim/audit に記録されるが SQL WHERE には未強制。Phase B でベース層に組み込み
- **図形編集 UI なし**：API は `PATCH /api/features/{id}` で `geometry` を受け取れるが、WebGIS の Draw/Modify UI は未実装
- **マイグレーションは手動適用**：Flyway 等のツールは未導入。`docker compose down -v` で再構築するのが現状の運用
- **WebView2 専用**：WebGIS は WebView2 ホスト下で動かす前提。ブラウザ単独起動も dev では動くが、bridge 通信は機能しない

## トラブルシュート

- **API が起動しない / `AGRI_GIS_JWT_SECRET` で fail-fast**: 32 バイト以上の secret を `$env:AGRI_GIS_JWT_SECRET` に設定。`AGRI_GIS_INITIAL_ADMIN_PW` も同様に必須
- **初回 admin でログインできない**: `InitialAdminBootstrap` のログを確認。admin が居なければ自動 upsert される。既に admin が居れば skip するため、忘れた場合は admin の再 upsert ではなく別 admin を `/api/admin/users` で発行する想定
- **401 が出続ける**: トークン期限切れ／署名鍵不一致を疑う。WinForms は自動で `LoginForm` を再表示。CLI からは `/api/auth/login` で再取得
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
