namespace AgriGis.Api.Tests.Fixtures;

// テストから HttpClient を組み立てるためのビルダ。
// WA5/A502: X-Actor 廃止に伴い WithActor は削除し、JWT Bearer ベースの
// WithActorAs("alice", "admin") を主用法とする。SeedUsers が seed したユーザを使う。
public sealed class ApiClientFactory
{
    private readonly ApiFactory _api;
    private string? _bearerToken;
    private string? _requestId;

    public ApiClientFactory(ApiFactory api) => _api = api;

    // SeedUsers 経由の固定ユーザに対応するトークンを発行して付与する。
    // role を別引数で渡せるのは Phase B の多ロール検証も視野に入れた拡張ポイント。
    public ApiClientFactory WithActorAs(string loginId, string role)
    {
        var (uid, dn) = loginId switch
        {
            SeedUsers.AliceLogin => (SeedUsers.AliceId, "Alice Admin"),
            SeedUsers.BobLogin   => (SeedUsers.BobId,   "Bob General"),
            SeedUsers.CarolLogin => (SeedUsers.CarolId, "Carol Guest"),
            _ => throw new InvalidOperationException($"unknown test user: {loginId}")
        };
        _bearerToken = TokenForge.Issue(
            userId: uid,
            loginId: loginId,
            displayName: dn,
            orgId: SeedUsers.OrgId,
            roles: new[] { role });
        return this;
    }

    public ApiClientFactory WithBearer(string token)
    {
        _bearerToken = token;
        return this;
    }

    public ApiClientFactory WithRequestId(string? requestId)
    {
        _requestId = requestId;
        return this;
    }

    public HttpClient Build()
    {
        var client = _api.CreateClient();
        if (_bearerToken is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_bearerToken}");
        }
        if (_requestId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", _requestId);
        }
        return client;
    }
}
