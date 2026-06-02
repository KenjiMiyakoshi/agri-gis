using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using AgriGis.Api.Options;
using AgriGis.Api.Tiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgriGis.Api.Endpoints;

// D201 (WD2): GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png proxy
// API 内 Bearer JWT 検証後、GeoServer に basic auth で proxy。
// theme 名は正規表現で validation。WebMercator (EPSG:3857) の z/x/y → BBOX 変換は
// WebMercatorTileMath に切り出し。
public static class TilesEndpoints
{
    private static readonly Regex ThemeNameRegex = new(@"^[a-z0-9_]{1,32}$", RegexOptions.Compiled);

    public static RouteGroupBuilder MapTilesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{layerId:int}/{theme}/{z:int}/{x:int}/{y:int}.png",
            async (int layerId, string theme, int z, int x, int y,
                   IHttpClientFactory httpClientFactory,
                   IOptions<GeoServerOptions> geoOpts,
                   CancellationToken ct) =>
        {
            // theme 名 validation
            if (!ThemeNameRegex.IsMatch(theme))
            {
                return Results.BadRequest(new
                {
                    type = "https://docs.agri-gis/errors/invalid-theme",
                    title = "Invalid theme name",
                    status = 400,
                    detail = $"theme must match ^[a-z0-9_]{{1,32}}$ (got '{theme}')"
                });
            }

            // z/x/y → bbox 変換 (EPSG:3857)
            (double minX, double minY, double maxX, double maxY) bbox;
            try
            {
                bbox = WebMercatorTileMath.TileToBbox3857(z, x, y);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.BadRequest(new { title = "Invalid z/x/y", detail = ex.Message });
            }

            // GeoServer WMS GetMap URL 構築
            var opts = geoOpts.Value;
            var url = $"{opts.BaseUrl.TrimEnd('/')}/{opts.Workspace}/wms" +
                      $"?service=WMS&version=1.1.1&request=GetMap" +
                      $"&layers={opts.Workspace}:l_{layerId}" +
                      $"&styles={opts.Workspace}:t_{theme}" +
                      $"&bbox={WebMercatorTileMath.FormatBboxArg(bbox.minX, bbox.minY, bbox.maxX, bbox.maxY)}" +
                      $"&width=256&height=256&srs=EPSG:3857&format=image/png&transparent=true";

            // basic auth で GeoServer に proxy
            var client = httpClientFactory.CreateClient("geoserver");
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{opts.AdminUser}:{opts.ResolveAdminPassword()}"));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            HttpResponseMessage geoResp;
            try
            {
                geoResp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(
                    title: "GeoServer unreachable",
                    detail: ex.Message,
                    statusCode: 503);
            }

            if (!geoResp.IsSuccessStatusCode)
            {
                var bodyText = await geoResp.Content.ReadAsStringAsync(ct);
                return Results.Problem(
                    title: "GeoServer returned error",
                    detail: $"status={(int)geoResp.StatusCode}; body={bodyText[..Math.Min(500, bodyText.Length)]}",
                    statusCode: 502);
            }

            var pngBytes = await geoResp.Content.ReadAsByteArrayAsync(ct);
            // Cache-Control: max-age=3600, public を付与して返す
            return (IResult)new TileFileResult(pngBytes);
        });
        // 認可: admin/general/guest 全て。Program.cs で MapGroup("/tiles").RequireAuthorization() で
        // Bearer 必須にする (個別 endpoint では AllowAnonymous しない)。

        return group;
    }
}

// D201: tile 応答に Cache-Control: max-age=3600, public ヘッダを付ける Result 実装
public sealed class TileFileResult : IResult
{
    private readonly byte[] _pngBytes;

    public TileFileResult(byte[] pngBytes) => _pngBytes = pngBytes;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "image/png";
        httpContext.Response.Headers.CacheControl = "max-age=3600, public";
        httpContext.Response.ContentLength = _pngBytes.Length;
        await httpContext.Response.Body.WriteAsync(_pngBytes);
    }
}
