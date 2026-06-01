# 0204: ProblemDetails + errors[] 拡張と例外マッピング

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0203 |
| Blocks | 0210, 0211, 0212 |

## 概要
ASP.NET Core の `ProblemDetails` をベースに `errors[]` 拡張を加えたエラー応答を返す共通機構を組む。PL/pgSQL 関数が投げる `SqlState` を HTTP ステータスにマップする。

## 背景・目的
案 B' は「エラー応答は ProblemDetails + errors[] 拡張」が固定仕様。属性スキーマ違反など複数フィールドのエラーを構造化して返す必要があり、サーバ側でマップを一元化する。

## スコープ
### 含む
- `Errors/ApiException.cs` (基底)
- `Errors/ValidationException.cs` (`IReadOnlyList<AttributeErrorDto>` を持つ → 422)
- `Errors/VersionConflictException.cs` (→ 409)
- `Errors/NotFoundException.cs` (→ 404)
- `Errors/MissingActorException.cs` (→ 400) は 0203 と統合
- `Middleware/ProblemDetailsMiddleware.cs`
  - PostgresException の `SqlState` を見て例外型に変換 + ProblemDetails に整形
  - `40001` → 409, `02000` → 404, `22023` → 400, `23503` → 404 など
  - `extensions.requestId` に X-Request-Id をセット
  - `extensions.errors` に AttributeErrorDto[] をセット（ある時のみ）
- `Program.cs` に `builder.Services.AddProblemDetails()` + 例外ミドルウェア追加

### 含まない
- 各エンドポイントでの個別バリデーション呼び出し（後続）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `MissingActorException` で 400 + `{ type, title, status:400, extensions: { requestId, errors:[...] } }`
- [ ] `VersionConflictException` で 409
- [ ] `NotFoundException` で 404
- [ ] `ValidationException` で 422 + errors[] が `[{attributeKey, code, message}]` 形式
- [ ] PostgresException(SqlState=40001) も同様に 409 に変換される
- [ ] ProblemDetails の `extensions.requestId` がレスポンスヘッダ `X-Request-Id` と一致

## 影響ファイル
- `D:\proj\agri-gis\api\Errors\ApiException.cs` (新規)
- `D:\proj\agri-gis\api\Errors\ValidationException.cs` (新規)
- `D:\proj\agri-gis\api\Errors\VersionConflictException.cs` (新規)
- `D:\proj\agri-gis\api\Errors\NotFoundException.cs` (新規)
- `D:\proj\agri-gis\api\Errors\MissingActorException.cs` (移設・新規)
- `D:\proj\agri-gis\api\Middleware\ProblemDetailsMiddleware.cs` (新規)
- `D:\proj\agri-gis\api\Program.cs` (services/middleware)

## 実装ノート
```csharp
// Middleware/ProblemDetailsMiddleware.cs (抜粋)
public async Task InvokeAsync(HttpContext ctx)
{
    try { await _next(ctx); }
    catch (Exception ex)
    {
        var (status, title, errors) = Map(ex);
        var pd = new ProblemDetails
        {
            Status = status,
            Title  = title,
            Type   = $"https://httpstatuses.io/{status}",
        };
        pd.Extensions["requestId"] = RequestContext.GetRequestId(ctx);
        if (errors is { Count: > 0 })
            pd.Extensions["errors"] = errors;

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(pd);
    }
}

private static (int, string, IReadOnlyList<AttributeErrorDto>?) Map(Exception ex) => ex switch
{
    MissingActorException        => (400, "X-Actor header is required", null),
    ValidationException v        => (422, "Validation failed", v.Errors),
    NotFoundException n          => (404, n.Message, null),
    VersionConflictException     => (409, "Version conflict", null),
    PostgresException pg when pg.SqlState == "40001" => (409, "Version conflict", null),
    PostgresException pg when pg.SqlState == "02000" => (404, "Not found", null),
    PostgresException pg when pg.SqlState == "22023" => (400, pg.MessageText, null),
    PostgresException pg when pg.SqlState == "23503" => (404, pg.MessageText, null),
    _                            => (500, "Internal Server Error", null)
};
```

注意点:
- `AddProblemDetails()` を呼んでおけば未捕捉例外用のデフォルトハンドラがあるが、`errors[]` 拡張を載せたいので独自ミドルウェアにする方が確実
- ミドルウェア順: `RequestContextMiddleware` → `ProblemDetailsMiddleware` → endpoints

## テスト観点
- 0303 / 0304: 各 HTTP コードのマッピング、ProblemDetails の JSON 形
- errors[] の構造（attributeKey, code, message）
