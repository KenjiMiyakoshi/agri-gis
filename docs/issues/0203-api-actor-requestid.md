# 0203: `X-Actor` / `X-Request-Id` ミドルウェアとヘルパ

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0201 |
| Blocks | 0204, 0207, 0210, 0211, 0212 |

## 概要
書き込み系エンドポイントで `X-Actor` ヘッダを必須にし、`X-Request-Id` が無ければサーバで UUID を採番してレスポンスとログに同期するための仕組みを用意する。

## 背景・目的
案 B' は監査ログとエラー応答に actor / request_id を載せる必要がある。各エンドポイントで毎回ヘッダから取り出すのは煩雑なのでヘルパ + ミドルウェアにまとめる。

## スコープ
### 含む
- `Middleware/RequestContextMiddleware.cs`
  - `HttpContext.Items["RequestId"]` を `X-Request-Id` ヘッダ or `Guid.NewGuid()` で埋める
  - レスポンスヘッダ `X-Request-Id` を必ずセット
- `Endpoints/RequestContext.cs` (static ヘルパ)
  - `RequireActor(HttpContext) -> string` (空なら例外 → 0204 で 400 マップ)
  - `RequireActor` は X-Actor ヘッダから取り出し、空白 trim、空なら `BadHttpRequestException("X-Actor header is required")`
  - `GetRequestId(HttpContext) -> string`
- `Program.cs` に `app.UseMiddleware<RequestContextMiddleware>()`

### 含まない
- ProblemDetails マッピング (0204)
- 認証（本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `X-Request-Id` ヘッダ付き GET でレスポンスに同じ ID が `X-Request-Id` で返ってくる
- [ ] 無しで叩いてもレスポンス `X-Request-Id` が新規 UUID で付く
- [ ] `RequireActor` が X-Actor 空時に例外を投げる
- [ ] `RequireActor` が空白だけの値で例外
- [ ] 2 回目以降の build エラーなし

## 影響ファイル
- `D:\proj\agri-gis\api\Middleware\RequestContextMiddleware.cs` (新規)
- `D:\proj\agri-gis\api\Endpoints\RequestContext.cs` (新規)
- `D:\proj\agri-gis\api\Program.cs` (UseMiddleware 追加)

## 実装ノート
```csharp
// Middleware/RequestContextMiddleware.cs
public sealed class RequestContextMiddleware
{
    private readonly RequestDelegate _next;
    public RequestContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var rid = ctx.Request.Headers["X-Request-Id"].ToString();
        if (string.IsNullOrWhiteSpace(rid)) rid = Guid.NewGuid().ToString("N");
        ctx.Items["RequestId"] = rid;
        ctx.Response.Headers["X-Request-Id"] = rid;
        await _next(ctx);
    }
}

// Endpoints/RequestContext.cs
public static class RequestContext
{
    public static string RequireActor(HttpContext ctx)
    {
        var v = ctx.Request.Headers["X-Actor"].ToString().Trim();
        if (string.IsNullOrEmpty(v))
            throw new MissingActorException();
        return v;
    }
    public static string GetRequestId(HttpContext ctx)
        => (string)ctx.Items["RequestId"]!;
}

public sealed class MissingActorException : Exception
{
    public MissingActorException() : base("X-Actor header is required") { }
}
```

注意点:
- `MissingActorException` は 0204 で 400 にマップする
- 読み取り系（GET）では `RequireActor` は呼ばない（要件: 書き込み系のみ必須）

## テスト観点
- 0304: X-Actor 無しの POST/PATCH/DELETE で 400 + errors[] が返る
- 0304: レスポンスに X-Request-Id ヘッダがある
