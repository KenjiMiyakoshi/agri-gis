# A203: IAuthorizationMiddlewareResultHandler で 401/403 を ProblemDetails 統合

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | A201 |
| Blocks | A204, A206 |

## 概要
`IAuthorizationMiddlewareResultHandler` をカスタム実装で差し替え、401/403 レスポンスを既存 ProblemDetails 形式 (0204) と統一する。

## 背景・目的
採択案「案 P」の API セクション:
> **`IAuthorizationMiddlewareResultHandler` 実装**で 401/403 を ProblemDetails に統合（OnChallenge 10 行ラッパは不採用）

OnChallenge ラッパ方式は JwtBearerEvents で個別実装が必要だが、`IAuthorizationMiddlewareResultHandler` 一発で AuthN/AuthZ 両方を集約できる。

## スコープ
### 含む
- `Auth/ProblemDetailsAuthorizationResultHandler.cs` 新規
- 401: type=`https://tools.ietf.org/html/rfc7235#section-3.1`, title=`Unauthorized`, status=401
- 403: type=`https://tools.ietf.org/html/rfc7231#section-6.5.3`, title=`Forbidden`, status=403
- trace_id / request_id（既存 0204 / 0203 と合わせる）
- `services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>()`

### 含まない
- JwtBearer の OnChallenge カスタマイズ（採択案で不採用）
- 既存 ProblemDetails ファクトリの新規作成（既存 0204 のものを再利用）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 未認証で `[Authorize]` エンドポイントを叩く → 401 + `Content-Type: application/problem+json`
- [ ] guest が書き込み系を叩く → 403 + `application/problem+json`
- [ ] レスポンスボディに `traceId` または `request_id` が含まれる
- [ ] WWW-Authenticate ヘッダは 401 のみ付与（403 では付けない）

## 影響ファイル
- `D:\proj\agri-gis\api\Auth\ProblemDetailsAuthorizationResultHandler.cs` (新規)
- `D:\proj\agri-gis\api\Program.cs`

## 実装ノート
```csharp
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler Default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context,
        AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Challenged)
        {
            await WriteProblem(context, 401, "Unauthorized",
                "Authentication required.",
                "https://tools.ietf.org/html/rfc7235#section-3.1");
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }
        if (result.Forbidden)
        {
            await WriteProblem(context, 403, "Forbidden",
                "You do not have permission to perform this action.",
                "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            return;
        }
        await Default.HandleAsync(next, context, policy, result);
    }

    private static async Task WriteProblem(HttpContext ctx, int status, string title, string detail, string type)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type, title, status, detail,
            traceId = ctx.TraceIdentifier,
            request_id = ctx.Request.Headers["X-Request-Id"].FirstOrDefault()
        };
        await ctx.Response.WriteAsJsonAsync(problem);
    }
}

// Program.cs
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler,
    ProblemDetailsAuthorizationResultHandler>();
```

注意点:
- 既存 0204 の ProblemDetails スキーマ (trace_id, request_id 等のキー名) と揃える
- JwtBearer の OnChallenge は default のまま (採択案明示)

## テスト観点
- A504 (AuthLoginTests / AuthRequiredTests): 401 時 `Content-Type: application/problem+json`
- A505 (AuthorizationTests): 403 時 `application/problem+json` + WWW-Authenticate 無し
