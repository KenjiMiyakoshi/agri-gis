# 認証・認可（Phase A）

agri-gis Phase A の認証基盤の運用ガイド。

## 全体像

- API は ASP.NET Core 8 Minimal API、認証は JWT Bearer (HS256)。
- WinForms クライアントは起動時に `LoginForm` でログイン → アクセストークンを `ISessionStore` (in-memory) に保持 → `BearerHandler` (`DelegatingHandler`) が全 HTTP リクエストの `Authorization` ヘッダにトークンを付与。
- 認可は role-based。3 役割固定: `admin` / `general` / `guest`。
- パスワードは BCrypt (work factor 11) で保存。

## JWT クレーム

`api/Auth/JwtService.cs` が発行する JWT のクレーム:

| claim | 内容 | 由来 |
|-------|------|------|
| `sub` | ユーザ UUID | `users.user_id` |
| `login_id` | ログイン ID | `users.login_id` |
| `display_name` | 表示名 | `users.display_name` |
| `org_id` | 所属組織 ID | `users.org_id` |
| `role` (複数可) | ロール | `user_roles.role` |
| `jti` | 一意 ID | ランダム UUID |
| `iss` | Issuer | env `AGRI_GIS_JWT_ISSUER` or `Jwt:Issuer` |
| `aud` | Audience | env `AGRI_GIS_JWT_AUDIENCE` or `Jwt:Audience` |
| `iat` / `nbf` / `exp` | 時刻 | TTL: env `AGRI_GIS_JWT_TTL_HOURS` or `Jwt:ExpiryHours`（既定 8 時間） |

`HttpContextCurrentUser` (`ICurrentUser`) がこれらを読み、エンドポイント引数として注入される。

## ロールマトリクス

| エンドポイント | admin | general | guest |
|---------------|:-----:|:-------:|:-----:|
| `GET    /api/health` | anonymous | anonymous | anonymous |
| `POST   /api/auth/login` | anonymous | anonymous | anonymous |
| `GET    /api/auth/me` | ○ | ○ | ○ |
| `POST   /api/auth/change-password` | ○ | ○ | ○ |
| `GET    /api/layers`,`/api/features` | ○ | ○ | ○ |
| `POST   /api/features` | ○ | ○ | 403 |
| `PATCH  /api/features/{id}` | ○ | ○ | 403 |
| `DELETE /api/features/{id}` | ○ | ○ | 403 |
| `PUT    /api/admin/layers/{id}/schema` | ○ | 403 | 403 |
| `*      /api/admin/organizations` | ○ | 403 | 403 |
| `*      /api/admin/users` | ○ | 403 | 403 |

書き込み系 (`POST/PATCH/DELETE /api/features`) は名前付きポリシー `WriteFeature` (admin or general) で制御。`/api/admin/*` は `RequireRole("admin")`。

## 鍵管理

| 環境変数 | 必須 | 用途 |
|----------|:----:|------|
| `AGRI_GIS_JWT_SECRET` | yes | HS256 署名鍵。**32 バイト以上**必須。起動時に未設定／短すぎる場合 fail-fast |
| `AGRI_GIS_JWT_ISSUER` | no | iss claim。既定 `agri-gis-api` |
| `AGRI_GIS_JWT_AUDIENCE` | no | aud claim。既定 `agri-gis-windows` |
| `AGRI_GIS_JWT_TTL_HOURS` | no | TTL 時間。既定 8 |
| `AGRI_GIS_INITIAL_ADMIN_PW` | yes | 初期 admin パスワード。`InitialAdminBootstrap` が起動時に確認、未設定 fail-fast |
| `AGRI_GIS_SKIP_BOOTSTRAP` | no | `1` で `InitialAdminBootstrap` をスキップ（テスト用） |

### secret 生成例

```bash
openssl rand -base64 48
```

PowerShell:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

## 起動シーケンス

1. `Program.cs` が `JwtService` の bootstrap インスタンスを作成 → `AGRI_GIS_JWT_SECRET` 検証で fail-fast。
2. `AddAuthentication().AddJwtBearer(...)` で `TokenValidationParameters` を構築。
3. `AddAuthorization` で `WriteFeature` ポリシー定義。
4. `InitialAdminBootstrap` (`IHostedService`) が起動時に `users` テーブルを確認、admin 居なければ既定組織 (`code='default'`) と admin ユーザを upsert（BCrypt ハッシュ）+ admin ロール付与。
5. Middleware: `CORS → Authentication → Authorization → RequestContext → ProblemDetails`。

## 認可失敗時の応答

`ProblemDetailsAuthorizationResultHandler` (`IAuthorizationMiddlewareResultHandler`) が 401/403 を `application/problem+json` 形式で返す:

```json
{
  "status": 401,
  "title": "Authentication required",
  "type": "https://httpstatuses.io/401",
  "extensions": { "requestId": "..." }
}
```

WinForms 側は `ApiClient.EnsureSuccessAsync` で 401 を `UnauthorizedApiException` に変換、`MainForm` が `LoginForm.ShowDialog()` で再ログインを促す。

## CORS

`WebGIS` (Vite, localhost:5173) のみ許可: `WithOrigins("http://localhost:5173","http://127.0.0.1:5173")`。Phase A では WebGIS が JWT を所持せず、ブラウザは GET のみで状態変更しない構成。

## テスト用トークン発行

`api.tests/Fixtures/TokenForge.cs`:

```csharp
var token = TokenForge.Issue(
    userId: SeedUsers.AliceId,
    loginId: "alice",
    displayName: "Alice Admin",
    orgId: SeedUsers.OrgId,
    roles: new[] { "admin" });
```

`ApiClientFactory.WithActorAs("alice","admin")` でラップしたヘルパも利用可。テスト用秘密鍵は `ApiFactory.TestJwtSecret` (`agri-gis-test-jwt-secret-32bytes!!__min__`) を環境変数に注入。

## Phase B 申し送り

- **refresh token + rotation**：Phase A は access 8h のみ、期限切れで再ログイン。
- **複数ロール兼務**：DB は多対多なので値追加だけで足りる。policy 側に `RequireRole("admin","general")` を追加するだけ。
- **テナント分離**：全 SQL `WHERE org_id = @currentOrg` を強制するベースクラスを導入予定。
- **`audit_log.actor` (TEXT) 列の rename**：Phase A は display_name snapshot として温存。Phase B で `display_name_snapshot` などへ rename。
- **WebGIS の JWT 引き渡し**：Phase A は CORS Origin 限定のみ。Phase B でブリッジ経由のトークン引き渡しを検討。
