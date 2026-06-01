# A206: 既存エンドポイントへの [Authorize] / [AllowAnonymous] 配置

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 1d |
| Depends on | A201, A202, A203, A204 |
| Blocks | A505 |

## 概要
既存の全エンドポイント (Layers/Features 系) に `RequireAuthorization()` を付与し、GET 系で公開すべきものに `AllowAnonymous` を付ける。書き込み系は guest 拒否を policy で実装する。

## 背景・目的
採択案「案 P」の API セクション:
> **AnonymousGuestMiddleware なし**、GET 系は `[AllowAnonymous]` で個別許可
> guest = JWT 必須だが書き込み系 403

## スコープ
### 含む
- 認可 Policy `"WriteAccess"` 定義: `admin` または `general` ロールが必要、guest 排除
- 全 GET エンドポイント: 既定で `RequireAuthorization()` を付ける（ただし採択案では「GET 系は AllowAnonymous で個別許可」とあるので、公開可能な GET にのみ AllowAnonymous）
  - 採択案の意図確認: 「guest = JWT 必須」なので **GET も基本は JWT 必須**、ただし匿名公開したいもの (例: 公開地図用) は AllowAnonymous で開ける
- 全書き込みエンドポイント (POST/PUT/PATCH/DELETE): `RequireAuthorization("WriteAccess")`
- Admin 系 (A301/A302) は別途 `"AdminOnly"` policy
- 既存全エンドポイントを 1 ファイルずつ確認、メタデータ付与

### 含まない
- /api/auth/* (A205 で既に付与済)
- /api/admin/* (A301/A302 で付与)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `WriteAccess` policy: roles に admin or general を含む
- [ ] guest JWT で POST /api/features → 403 ProblemDetails
- [ ] anonymous (Authorization 無し) で POST /api/features → 401
- [ ] guest JWT で GET /api/features → 200 (読み取りは guest OK)
- [ ] anonymous で GET /api/features → 401 (採択案: guest = JWT 必須)
- [ ] AllowAnonymous を明示したエンドポイント (もしあれば) は anonymous OK
- [ ] 既存 17 テストのうち WithActor("alice") は admin role で動くので write 200

## 影響ファイル
- `D:\proj\agri-gis\api\Program.cs` (policy 登録)
- `D:\proj\agri-gis\api\Endpoints\LayersEndpoints.cs`
- `D:\proj\agri-gis\api\Endpoints\FeaturesEndpoints.cs`
- `D:\proj\agri-gis\api\Endpoints\LayerSchemaEndpoints.cs`
- 他、既存 MapGroup 系全部

## 実装ノート
```csharp
// Program.cs
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("WriteAccess", p =>
        p.RequireAuthenticatedUser()
         .RequireAssertion(ctx =>
             ctx.User.FindAll("role").Any(c => c.Value is "admin" or "general")));
    opts.AddPolicy("AdminOnly", p =>
        p.RequireAuthenticatedUser()
         .RequireRole("admin"));
});

// FeaturesEndpoints.cs
public static void Map(IEndpointRouteBuilder app)
{
    var g = app.MapGroup("/api").RequireAuthorization();   // 既定で JWT 必須
    g.MapGet("/features", ...);                            // guest OK (RequireAuthorization 継承)
    g.MapPost("/features", ...).RequireAuthorization("WriteAccess");
    g.MapPatch("/features/{id}", ...).RequireAuthorization("WriteAccess");
    g.MapDelete("/features/{id}", ...).RequireAuthorization("WriteAccess");
}
```

注意点:
- GET 系を明示的に `AllowAnonymous` にするケースは Phase A では作らない（公開地図用途は Phase B 以降）。採択案の「GET 系は AllowAnonymous で個別許可」は **必要に応じて個別開放できる方式** と解釈し、デフォルト JWT 必須。
- A505 の AuthorizationTests で 3 role × 全エンドポイントを matrix 検証

## テスト観点
- A505 (AuthorizationTests): admin/general/guest × GET/POST/PATCH/DELETE の matrix
  - admin: all 200
  - general: all 200 (Phase A では admin と同等の書き込み権限)
  - guest: GET 200, write 403
  - anonymous: 全 401
