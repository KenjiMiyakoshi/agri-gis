namespace AgriGis.Api.Tests.Fixtures;

// テストから HttpClient を組み立てるためのビルダ。
// `.WithActor("alice").WithRequestId("rid-1").Build()` のように使う。
public sealed class ApiClientFactory
{
    private readonly ApiFactory _api;
    private string? _actor;
    private string? _requestId;

    public ApiClientFactory(ApiFactory api) => _api = api;

    public ApiClientFactory WithActor(string? actor)
    {
        _actor = actor;
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
        if (_actor is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Actor", _actor);
        }
        if (_requestId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", _requestId);
        }
        return client;
    }
}
