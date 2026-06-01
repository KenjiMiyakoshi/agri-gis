# A204: RequestContext から RequireActor 廃止 + middleware 順序見直し

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | A202, A203 |
| Blocks | A205, A206 |

## 概要
`RequestContext.RequireActor` を削除し、`ICurrentUser` 経由に統一する。`UseCors → UseAuthentication → UseAuthorization → RequestContext → ProblemDetails` の正しい middleware 順序に揃える。

## 背景・目的
採択案「案 P」の API セクション:
> `RequestContext.RequireActor` 廃止、`ICurrentUser` を Endpoint 引数で受ける
> **AnonymousGuestMiddleware なし**、GET 系は `[AllowAnonymous]` で個別許可
> middleware 順序: `UseCors → UseAuthentication → UseAuthorization → RequestContext → ProblemDetails`

## スコープ
### 含む
- `RequestContext` クラスから `Actor` プロパティ / `RequireActor` メソッド削除
- `RequestContext` には `RequestId` のみ残す（X-Request-Id ヘッダ伝搬用）
- `RequestContextMiddleware` を Authentication/Authorization の後に移動
- ProblemDetails middleware を最後 (例外ハンドラ最外周)
- X-Actor ヘッダ参照箇所をすべて削除（A403 の WinForms 側削除と対称）

### 含まない
- AnonymousGuestMiddleware 作成（採択案で不採用）
- 個別エンドポイントの [Authorize] 配置 (A206)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `RequestContext` に `Actor` / `RequireActor` が存在しない
- [ ] X-Actor ヘッダを送っても無視される（読まない）
- [ ] middleware 順序が `UseCors → UseAuthentication → UseAuthorization → RequestContext → ProblemDetails`
- [ ] 既存 RequestId 伝搬テスト (0203 系) が green
- [ ] Endpoint シグネチャから `RequestContext.RequireActor()` 呼び出しが全削除

## 影響ファイル
- `D:\proj\agri-gis\api\Infrastructure\RequestContext.cs`
- `D:\proj\agri-gis\api\Infrastructure\RequestContextMiddleware.cs`
- `D:\proj\agri-gis\api\Program.cs`
- 既存 Endpoint ファイル (Features/Layers 等): `RequireActor()` 呼び出しを `ICurrentUser.LoginId` または `.DisplayName` 参照に置換

## 実装ノート
```csharp
// 旧
public sealed class RequestContext
{
    public string? Actor { get; set; }
    public string Identity => Actor ?? throw new InvalidOperationException("actor missing");
    public string RequireActor() => Actor ?? throw new ProblemException(400, "actor missing");
}

// 新
public sealed class RequestContext
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
}

// Program.cs middleware 順序
app.UseCors(...);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RequestContextMiddleware>();   // RequestId のみ
app.UseExceptionHandler(...);                    // ProblemDetails
```

エンドポイントの書き換え例:
```csharp
// 旧
app.MapPost("/api/features", (RequestContext ctx, FeatureCreateDto dto) =>
{
    var actor = ctx.RequireActor();
    ...
});

// 新
app.MapPost("/api/features", (ICurrentUser me, FeatureCreateDto dto) =>
{
    // me.LoginId / me.UserId を使う
    ...
}).RequireAuthorization();
```

注意点:
- 既存テスト `MissingActorTests` (X-Actor 欠落で 400 を期待) は A503 で `AuthRequiredTests` (JWT 欠落で 401) にリネーム + 期待値更新

## テスト観点
- A503 (AuthRequiredTests): X-Actor だけ送って Authorization 無し → 401（X-Actor は無視される）
- 既存 0203 の RequestId 伝搬テストが green
- middleware 順序ミスがあると CORS preflight が認証で 401 になる → smoke 確認
