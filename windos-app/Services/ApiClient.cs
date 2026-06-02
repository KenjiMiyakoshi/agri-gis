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

    // D205 (WD2): GetFeaturesAsync は Phase D で削除済 (IApiClient interface からも除去)。
    // 全件 GeoJSON 取得経路は Phase D で TileLayer に切替、WebGIS 側 D303 で API も 410 化予定。

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

    // ----- WB3 B401: AdminLayers CRUD -----

    public async Task<IReadOnlyList<LayerAdminDto>> ListLayersAdminAsync(bool includeDeleted, CancellationToken ct)
    {
        var url = $"/api/admin/layers?includeDeleted={(includeDeleted ? "true" : "false")}";
        using var res = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(res, ct);
        var list = await res.Content.ReadFromJsonAsync<List<LayerAdminDto>>(JsonOpts, ct);
        return list ?? new List<LayerAdminDto>();
    }

    public async Task<LayerAdminDto> CreateLayerAsync(CreateLayerRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync("/api/admin/layers", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LayerAdminDto>(JsonOpts, ct))!;
    }

    public async Task<LayerAdminDto> UpdateLayerAsync(int layerId, UpdateLayerRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var msg = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/layers/{layerId}") { Content = content };
        using var res = await _http.SendAsync(msg, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LayerAdminDto>(JsonOpts, ct))!;
    }

    public async Task DeleteLayerAsync(int layerId, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync($"/api/admin/layers/{layerId}", ct);
        await EnsureSuccessAsync(res, ct);
    }

    // ----- WB3 B401: Import jobs + bulk -----

    public async Task<ImportJobDto> StartImportJobAsync(int layerId, StartImportJobRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync($"/api/admin/layers/{layerId}/import-jobs", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<ImportJobDto>(JsonOpts, ct))!;
    }

    public async Task<ImportJobDto> GetImportJobAsync(Guid jobId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"/api/admin/layers/import-jobs/{jobId}", ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<ImportJobDto>(JsonOpts, ct))!;
    }

    public async Task<ImportJobDto> FinalizeImportJobAsync(Guid jobId, FinalizeImportJobRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync($"/api/admin/layers/import-jobs/{jobId}/finalize", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<ImportJobDto>(JsonOpts, ct))!;
    }

    public async Task<BulkFeaturesResponseDto> BulkInsertFeaturesAsync(
        int layerId, BulkFeaturesRequestDto req, CancellationToken ct)
    {
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync($"/api/admin/layers/{layerId}/features/bulk", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<BulkFeaturesResponseDto>(JsonOpts, ct))!;
    }

    // ----- D401 (WD4): Phase D Selection / Theme / Logout -----

    public async Task<CreateSelectionResponseDto> CreateSelectionAsync(
        IReadOnlyList<Guid> entityIds, string? colorHex, CancellationToken ct)
    {
        var req = new CreateSelectionRequestDto(entityIds, colorHex);
        using var content = JsonContent.Create(req, options: JsonOpts);
        using var res = await _http.PostAsync("/api/selection", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<CreateSelectionResponseDto>(JsonOpts, ct))!;
    }

    public async Task DeleteSelectionAsync(Guid sid, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync($"/api/selection/{sid}", ct);
        await EnsureSuccessAsync(res, ct);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        using var content = new StringContent("", Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync("/api/auth/logout", content, ct);
        await EnsureSuccessAsync(res, ct);
    }

    public async Task<LayerStyleDto> GetLayerStyleAsync(int layerId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"/api/admin/layers/{layerId}/style", ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LayerStyleDto>(JsonOpts, ct))!;
    }

    public async Task<LayerStyleDto> UpdateLayerStyleAsync(int layerId, LayerStyleDto style, CancellationToken ct)
    {
        using var content = JsonContent.Create(style, options: JsonOpts);
        using var res = await _http.PutAsync($"/api/admin/layers/{layerId}/style", content, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<LayerStyleDto>(JsonOpts, ct))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;

        var body = await res.Content.ReadAsStringAsync(ct);
        var problem = ProblemDetailsParser.Parse(body);

        // A404: 401 はトークン失効と見做して MainForm の再ログインフローへ
        if ((int)res.StatusCode == 401)
        {
            throw new UnauthorizedApiException(problem);
        }

        var title = problem.Title ?? res.ReasonPhrase ?? $"HTTP {(int)res.StatusCode}";
        throw new ApiException($"{(int)res.StatusCode} {title}", problem);
    }
}
