using System.Net.Http.Headers;
using System.Text;
using AgriGis.Api.Options;
using Microsoft.Extensions.Options;

namespace AgriGis.Api.Auth;

// D203 (WD2): IGeoServerStyleSync の HTTP/REST 実装。
//
// 注意 (Phase D MVP): SLD XML の生成は WD2 の skeleton 段階では最小限 (ThemeStyleDto → SLD)。
// 完全な SLD パターン集 (5 例) は WD5 D601 で docs/rendering.md に追加。
// 朝のユーザーレビュー後に肉付け予定。
public sealed class GeoServerStyleSync : IGeoServerStyleSync
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeoServerOptions _opts;
    private readonly ILogger<GeoServerStyleSync> _log;

    public GeoServerStyleSync(
        IHttpClientFactory httpClientFactory,
        IOptions<GeoServerOptions> opts,
        ILogger<GeoServerStyleSync> log)
    {
        _httpClientFactory = httpClientFactory;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<bool> PushStyleAsync(int layerId, string themeName, string sldXml, CancellationToken ct)
    {
        // theme 別 style 名は GeoServer 内で t_{themeName} (TilesEndpoints と整合)
        var styleName = $"t_{themeName}";
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/rest/workspaces/{_opts.Workspace}/styles/{styleName}";

        var client = _httpClientFactory.CreateClient("geoserver");
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{_opts.AdminUser}:{_opts.ResolveAdminPassword()}"));

        // 1) 既存 style が存在するか GET で確認
        // (GeoServer REST は HEAD を 405 Method Not Allowed で返すケースがあり信頼できないため
        //  GET で 200 (存在) / 404 (不在) を判定する。Phase D bug fix)
        bool exists = false;
        using (var checkReq = new HttpRequestMessage(HttpMethod.Get, url))
        {
            checkReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            // Accept header 未指定だと GeoServer が SLD content-type negotiation で 500 を返すケースがある
            checkReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                using var checkResp = await client.SendAsync(checkReq, ct);
                exists = checkResp.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "GeoServer GET style failed (treating as not-exists): {Url}", url);
                exists = false;
            }
        }

        // 2) 存在しなければ POST で作成、存在すれば PUT で更新
        var method = exists ? HttpMethod.Put : HttpMethod.Post;
        var targetUrl = exists
            ? url
            : $"{_opts.BaseUrl.TrimEnd('/')}/rest/workspaces/{_opts.Workspace}/styles?name={styleName}";

        using var bodyReq = new HttpRequestMessage(method, targetUrl);
        bodyReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        bodyReq.Content = new StringContent(sldXml, Encoding.UTF8);
        bodyReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ogc.sld+xml");

        try
        {
            using var resp = await client.SendAsync(bodyReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await resp.Content.ReadAsStringAsync(ct);
                _log.LogError(
                    "GeoServer style push failed: layerId={LayerId} theme={Theme} status={Status} body={Body}",
                    layerId, themeName, (int)resp.StatusCode, bodyText[..Math.Min(500, bodyText.Length)]);
                return false;
            }
            return true;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex,
                "GeoServer style push failed (network): layerId={LayerId} theme={Theme}",
                layerId, themeName);
            return false;
        }
    }
}
