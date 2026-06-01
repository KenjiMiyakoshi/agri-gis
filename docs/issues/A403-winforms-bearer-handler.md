# A403: BearerHandler + ApiClient.LoginAsync + X-Actor 削除

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 0.5d |
| Depends on | A205, A401 |
| Blocks | A402, A404 |

## 概要
新規 `Services/BearerHandler.cs` (`DelegatingHandler`) で HTTP 要求に `Authorization: Bearer <token>` を自動付与し、既存 X-Actor 送出を削除する。`ApiClient.LoginAsync` を追加。

## 背景・目的
採択案「案 P」の WinForms セクション:
> 新規 `windos-app/Services/BearerHandler.cs` (`DelegatingHandler`) で Authorization: Bearer 付与
> X-Actor ヘッダ送出は削除（混在防止）

## スコープ
### 含む
- `Services/BearerHandler.cs`: `ISessionStore.Current?.AccessToken` を読み Authorization ヘッダに付与
- `ApiClient.LoginAsync(string loginId, string password)`: `POST /api/auth/login` 呼び出し
- `ApiClient` の HttpClient に BearerHandler を inner handler として組み込み
- 既存 X-Actor を付与していた箇所をすべて削除
- 既存 `ApiClient.SetActor(...)` 系メソッドも削除

### 含まない
- 401 ハンドリング (A404)
- リフレッシュ (Phase B)

## 受け入れ条件 (Acceptance Criteria)
- [ ] Session が NULL ならリクエストに Authorization ヘッダが付かない
- [ ] Session 有り → 全リクエストに `Authorization: Bearer <token>`
- [ ] X-Actor ヘッダが送出されない（すべてのリクエストで）
- [ ] `ApiClient.LoginAsync` 成功時 LoginResponse (access_token, expires_at, user_id, login_id, display_name, org_id, roles) を返す
- [ ] LoginAsync は BearerHandler を経由するが、Session 未設定時は素通り (anonymous endpoint なので OK)

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Services\BearerHandler.cs` (新規)
- `D:\proj\agri-gis\windos-app\Services\ApiClient.cs` (LoginAsync 追加、X-Actor 削除)
- `D:\proj\agri-gis\windos-app\Program.cs` (HttpClient builder に BearerHandler 登録)

## 実装ノート
```csharp
// Services/BearerHandler.cs
public sealed class BearerHandler : DelegatingHandler
{
    private readonly ISessionStore _session;
    public BearerHandler(ISessionStore session) { _session = session; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var s = _session.Current;
        if (s is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        return base.SendAsync(request, ct);
    }
}

// Services/ApiClient.cs (抜粋)
public async Task<LoginResponse> LoginAsync(string loginId, string password)
{
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { login_id = loginId, password })
    };
    using var res = await _http.SendAsync(req);
    if (!res.IsSuccessStatusCode)
        throw new ApiException((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    return await res.Content.ReadFromJsonAsync<LoginResponse>()
        ?? throw new InvalidOperationException("empty login response");
}

// Program.cs (HttpClientFactory)
services.AddTransient<BearerHandler>();
services.AddHttpClient<ApiClient>()
        .AddHttpMessageHandler<BearerHandler>();
```

LoginResponse DTO は API の戻りに合わせる:
```csharp
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles);
```

注意点:
- LoginAsync は ApiClient のメソッドだが、トークン取得前なので Session = null。BearerHandler は null チェックで素通り
- JSON snake_case のため `JsonNamingPolicy.SnakeCaseLower` を HttpClient の JsonSerializerOptions に設定（あるいは `[JsonPropertyName]`）

## テスト観点
- 手動: Fiddler 等で Authorization: Bearer が付き、X-Actor が無いことを確認
- ApiClient のユニットテスト（既存 0503 があれば）で BearerHandler 経由を mock 検証
