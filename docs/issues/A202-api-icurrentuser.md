# A202: ICurrentUser interface + HttpContextCurrentUser

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | A201 |
| Blocks | A204, A205, A206, A301, A302 |

## 概要
`ICurrentUser` インターフェースと `HttpContextCurrentUser` 実装を 1 セット追加し、DI 登録する。`FakeCurrentUser` は作らない（テストは JWT 発行で実体経由）。

## 背景・目的
採択案「案 P」の API セクション:
> `ICurrentUser` interface（`HttpContextCurrentUser` 1 実装、DI 登録、`FakeCurrentUser` は作らない）
> `RequestContext.RequireActor` 廃止、`ICurrentUser` を Endpoint 引数で受ける

## スコープ
### 含む
- `Auth/ICurrentUser.cs`: `UserId (Guid)`, `LoginId (string)`, `DisplayName (string)`, `OrgId (int)`, `Roles (IReadOnlyList<string>)`, `IsGuest (bool)`, `IsAdmin (bool)` プロパティ
- `Auth/HttpContextCurrentUser.cs`: `HttpContext.User` から claims を読む実装
- `Program.cs` で `AddScoped<ICurrentUser, HttpContextCurrentUser>()` 登録
- claims 取得時に `sub` 不在なら `InvalidOperationException`

### 含まない
- `FakeCurrentUser` (作らない、採択案明示)
- ProblemDetails への 401/403 統合 (A203)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `ICurrentUser` を Endpoint で `[FromServices]` または直接引数で受け取れる
- [ ] `sub` claim が UUID として `UserId` に
- [ ] `name` claim が `LoginId` に
- [ ] `role` claim 複数値が `Roles` に
- [ ] `org_id` claim が `OrgId` に
- [ ] `IsGuest = Roles.Contains("guest")`, `IsAdmin = Roles.Contains("admin")`
- [ ] HttpContext.User が未認証なら `UserId` アクセスで例外（呼び出し前に [Authorize] で弾く前提）

## 影響ファイル
- `D:\proj\agri-gis\api\Auth\ICurrentUser.cs` (新規)
- `D:\proj\agri-gis\api\Auth\HttpContextCurrentUser.cs` (新規)
- `D:\proj\agri-gis\api\Program.cs`

## 実装ノート
```csharp
// Auth/ICurrentUser.cs
public interface ICurrentUser
{
    Guid UserId { get; }
    string LoginId { get; }
    string DisplayName { get; }
    int OrgId { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsGuest { get; }
    bool IsAdmin { get; }
}

// Auth/HttpContextCurrentUser.cs
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUser(IHttpContextAccessor accessor) { _accessor = accessor; }

    private ClaimsPrincipal Principal =>
        _accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HttpContext / ClaimsPrincipal");

    public Guid UserId =>
        Guid.Parse(Principal.FindFirstValue("sub")
                   ?? throw new InvalidOperationException("sub claim missing"));
    public string LoginId =>
        Principal.FindFirstValue("name")
        ?? throw new InvalidOperationException("name claim missing");
    public string DisplayName =>
        Principal.FindFirstValue("display_name") ?? LoginId;
    public int OrgId =>
        int.Parse(Principal.FindFirstValue("org_id")
                  ?? throw new InvalidOperationException("org_id claim missing"));
    public IReadOnlyList<string> Roles =>
        Principal.FindAll("role").Select(c => c.Value).ToList();
    public bool IsGuest => Roles.Contains("guest");
    public bool IsAdmin => Roles.Contains("admin");
}

// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
```

注意点:
- `display_name` claim は A205 で発行、無ければ LoginId にフォールバック
- Endpoint シグネチャ例: `app.MapGet("/x", (ICurrentUser me) => ...)`

## テスト観点
- A504 (AuthLoginTests): /api/auth/me が ICurrentUser から正しい値を返す
- A505 (AuthorizationTests): IsGuest / IsAdmin 判定が claims と一致
