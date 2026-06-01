using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgriGis.Desktop.Core;
using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

// HttpClient ラッパ。actor / If-Match / Request-Id の付与と
// ProblemDetails のエラーマッピングを担う。
public sealed class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<LoginResponseDto> LoginAsync(string loginId, string password, CancellationToken ct)
    {
        var req = new LoginRequestDto(loginId, password);
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync("/api/auth/login", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOpts, ct))!;
    }

    public async Task<IReadOnlyList<LayerDto>> GetLayersAsync(CancellationToken ct)
    {
        using var res = await _http.GetAsync("/api/layers", ct);
        await EnsureSuccessAsync(res, ct);
        var list = await res.Content.ReadFromJsonAsync<List<LayerDto>>(JsonOpts, ct);
        return list ?? new List<LayerDto>();
    }

    public async Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"/api/layers/{layerId}/schema", ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LayerSchemaResponseDto>(JsonOpts, ct))!;
    }

    public async Task<FeatureCollectionDto> GetFeaturesAsync(int layerId, DateOnly? asOf, CancellationToken ct)
    {
        var url = $"/api/features?layerId={layerId}";
        if (asOf is { } d)
        {
            url += $"&asOf={d:yyyy-MM-dd}";
        }
        using var res = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<FeatureCollectionDto>(JsonOpts, ct))!;
    }

    public async Task<FeatureDto> GetFeatureAsync(Guid entityId, DateOnly? asOf, CancellationToken ct)
    {
        var url = $"/api/features/{entityId}";
        if (asOf is { } d)
        {
            url += $"?asOf={d:yyyy-MM-dd}";
        }
        using var res = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<FeatureDto>(JsonOpts, ct))!;
    }

    public async Task<CreateFeatureResultDto> CreateFeatureAsync(
        CreateFeatureRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/features") { Content = content };

        using var res = await _http.SendAsync(msg, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<CreateFeatureResultDto>(JsonOpts, ct))!;
    }

    public async Task<PatchFeatureResultDto> UpdateFeatureAsync(
        Guid entityId, UpdateFeatureRequestDto req, int ifMatchVersion, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var msg = new HttpRequestMessage(HttpMethod.Patch, $"/api/features/{entityId}") { Content = content };
        // サーバ側は素の数値 (例: "1") を int.TryParse で読む実装 (FeatureEndpoints.cs)。
        // ETag 形式 ("\"1\"") を二重送出すると HTTP 結合値 "1, \"1\"" になり 428 を引く。
        msg.Headers.TryAddWithoutValidation("If-Match", ifMatchVersion.ToString());

        using var res = await _http.SendAsync(msg, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<PatchFeatureResultDto>(JsonOpts, ct))!;
    }

    public async Task DeleteFeatureAsync(Guid entityId, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Delete, $"/api/features/{entityId}");
        using var res = await _http.SendAsync(msg, ct);
        await EnsureSuccessAsync(res, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;

        var body = await res.Content.ReadAsStringAsync(ct);
        var problem = ProblemDetailsParser.Parse(body);
        var title = problem.Title ?? res.ReasonPhrase ?? $"HTTP {(int)res.StatusCode}";
        throw new ApiException($"{(int)res.StatusCode} {title}", problem);
    }
}
