using AgriGis.Desktop.Auth;

namespace AgriGis.Desktop.Services;

// ApiClient の HttpClient に挿入される DelegatingHandler。
// ログイン済みなら Authorization: Bearer {AccessToken} を全リクエストに付与する。
// 未ログインや login エンドポイント自身では何もしない (サーバが 401 を返す想定)。
public sealed class BearerHandler : DelegatingHandler
{
    private readonly ISessionStore _store;

    public BearerHandler(ISessionStore store)
    {
        _store = store;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var session = _store.Current;
        if (session is not null && request.Headers.Authorization is null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {session.AccessToken}");
        }
        return base.SendAsync(request, cancellationToken);
    }
}
