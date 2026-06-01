# 0503: WinForms `Services/` (IApiClient/ApiClient, IBridgeMessenger/BridgeMessenger)

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 1d |
| Depends on | 0501, 0502 |
| Blocks | 0504 |

## 概要
HTTP API クライアントと WebView2 ブリッジ送信ヘルパを `Services/` に実装する。

## 背景・目的
案 B' で Services は I/O を含む層。Form から直接 HttpClient / WebView2 を触らせず、テスト時にモック差し替え可能にする。

## スコープ
### 含む
- `Services/IApiClient.cs`
  - `Task<IReadOnlyList<LayerDto>> GetLayersAsync(CT)`
  - `Task<LayerSchema> GetLayerSchemaAsync(int layerId, CT)`
  - `Task<FeatureCollectionDto> GetFeaturesAsync(int layerId, DateOnly? asOf, CT)`
  - `Task<FeatureDto> GetFeatureAsync(Guid entityId, CT)`
  - `Task<CreateFeatureResult> CreateFeatureAsync(CreateFeatureRequest req, string actor, CT)`
  - `Task<int> UpdateFeatureAsync(Guid entityId, UpdateFeatureRequest req, int ifMatchVersion, string actor, CT)`
  - `Task DeleteFeatureAsync(Guid entityId, string actor, CT)`
- `Services/ApiClient.cs`
  - `HttpClient` をコンストラクタ DI
  - `X-Actor`, `If-Match` ヘッダの付与は呼び出しごと
  - レスポンスエラー時は `ProblemDetailsParser.Parse` の結果を `ApiException` に詰めて throw
- `Services/IBridgeMessenger.cs`
  - `void Send(string type, object payload, string? requestId = null)`
- `Services/BridgeMessenger.cs`
  - `CoreWebView2` をコンストラクタで受ける（Forms 側がセットアップ後に渡す）
  - `Send` で envelope を JSON 化 + `PostWebMessageAsString`
  - 受信は `MessageReceived` イベントを公開（`event EventHandler<EnvelopeReceived>`）
- DTO は WinForms 用に `Dto/` に置く（API の record と並列、命名一致）
- `Program.cs` で `AddHttpClient<IApiClient, ApiClient>(c => c.BaseAddress = new Uri(...))`

### 含まない
- Form 実装 (0504)
- 認証

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet build` が通る
- [ ] `IApiClient.GetLayersAsync` が API から `LayerDto[]` を取れる手動確認
- [ ] エラー応答時に `ApiException.ParsedProblem` で詳細にアクセスできる
- [ ] `BridgeMessenger.Send("layer_select", new { layerId=1 })` が JSON envelope を WebView に送る

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Services\IApiClient.cs` (新規)
- `D:\proj\agri-gis\windos-app\Services\ApiClient.cs` (新規)
- `D:\proj\agri-gis\windos-app\Services\ApiException.cs` (新規)
- `D:\proj\agri-gis\windos-app\Services\IBridgeMessenger.cs` (新規)
- `D:\proj\agri-gis\windos-app\Services\BridgeMessenger.cs` (新規)
- `D:\proj\agri-gis\windos-app\Dto\*.cs` (新規, API の record と同名)
- `D:\proj\agri-gis\windos-app\Program.cs` (DI)

## 実装ノート
```csharp
public sealed class ApiException : Exception
{
    public ProblemDetailsParser.ParsedProblem Problem { get; }
    public ApiException(string msg, ProblemDetailsParser.ParsedProblem p) : base(msg) => Problem = p;
}

public sealed class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<LayerDto>> GetLayersAsync(CancellationToken ct)
    {
        var res = await _http.GetAsync("/api/layers", ct);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<List<LayerDto>>(JsonOpts, ct))!;
    }
    // ... 各メソッド

    private static async Task EnsureSuccess(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync();
        var pp = ProblemDetailsParser.Parse(body);
        throw new ApiException($"HTTP {(int)res.StatusCode}", pp);
    }
}
```

```csharp
public sealed class BridgeMessenger : IBridgeMessenger, IDisposable
{
    private readonly CoreWebView2 _webview;
    public event EventHandler<Envelope>? MessageReceived;

    public BridgeMessenger(CoreWebView2 wv)
    {
        _webview = wv;
        _webview.WebMessageReceived += (_, e) =>
        {
            var json = e.TryGetWebMessageAsString();
            try { MessageReceived?.Invoke(this, JsonSerializer.Deserialize<Envelope>(json)!); }
            catch { /* ログ */ }
        };
    }
    public void Send(string type, object payload, string? requestId = null)
    {
        var env = new Envelope(type, payload, requestId);
        _webview.PostWebMessageAsString(JsonSerializer.Serialize(env, JsonOpts));
    }
    public void Dispose() { /* unsubscribe */ }
}

public sealed record Envelope(string Type, object Payload, string? RequestId);
```

注意点:
- `HttpClient` の `BaseAddress` は appsettings 系で持つか、`Program.cs` でハードコード ("http://localhost:5080")
- `CancellationToken` を必ず受ける

## テスト観点
- 0505: ApiClient のモック化はせず、ApiException の生成だけ単体で確認できればよい（ProblemDetailsParser 経由）
