using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using AgriGis.Api.Auth;
using AgriGis.Api.Options;
using AgriGis.Api.Shared;
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
            async (int layerId, string theme, int z, int x, int y, string? asOf, string? sv,
                   IHttpClientFactory httpClientFactory,
                   IOptions<GeoServerOptions> geoOpts,
                   ICurrentUser user,
                   ILayerPermissionService perm,
                   CancellationToken ct) =>
        {
            // E205 (WE2): asOf 対応
            // D'102 (WD'1): sv は URL 一意性のためのみ受領 (API ロジックには使わない、cache key を変える役割)
            _ = sv;

            // F205 (Phase F WF2): URL 直叩き対策 (深層防御)。
            // /api/layers の org フィルタを WebGIS 側でバイパスして tile を直接叩かれた場合に備える。
            // admin は service 内で bypass。
            if (!await perm.CanViewAsync(user.OrgId, layerId, user.Roles, ct))
            {
                return Results.Problem(
                    type: "https://docs.agri-gis/errors/layer-permission-denied",
                    title: "View denied for this layer",
                    detail: $"Your organization does not have can_view for layer {layerId}.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var asOfDate = AsOfParser.TryParse(asOf);
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
            // D201 hotfix + E205 (WE2): asOf 分岐
            //   asOf 無し → feature_current featureType + CQL_FILTER=layer_id=N (Phase D 既存)
            //   asOf あり → feature_asof featureType + CQL_FILTER に valid_from/_to 追加
            var opts = geoOpts.Value;
            string featureType;
            string cqlFilter;
            if (asOfDate is null)
            {
                featureType = "feature_current";
                cqlFilter = Uri.EscapeDataString($"layer_id={layerId}");
            }
            else
            {
                var asOfStr = asOfDate.Value.ToString("yyyy-MM-dd");
                featureType = "feature_asof";
                cqlFilter = Uri.EscapeDataString(
                    $"layer_id={layerId} AND valid_from <= '{asOfStr}' AND '{asOfStr}' < valid_to");
            }
            var url = $"{opts.BaseUrl.TrimEnd('/')}/{opts.Workspace}/wms" +
                      $"?service=WMS&version=1.1.1&request=GetMap" +
                      $"&layers={opts.Workspace}:{featureType}" +
                      $"&styles={opts.Workspace}:t_{theme}" +
                      $"&bbox={WebMercatorTileMath.FormatBboxArg(bbox.minX, bbox.minY, bbox.maxX, bbox.maxY)}" +
                      $"&width=256&height=256&srs=EPSG:3857&format=image/png&transparent=true" +
                      $"&CQL_FILTER={cqlFilter}";

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
            // E205 (WE2): asOf あり時は Cache-Control: no-store (履歴 cache 肥大化防止)
            return (IResult)new TileFileResult(pngBytes, noStore: asOfDate is not null);
        });
        // 認可: admin/general/guest 全て。Program.cs で MapGroup("/tiles").RequireAuthorization() で
        // Bearer 必須にする (個別 endpoint では AllowAnonymous しない)。

        return group;
    }
}

// D201 + E205: tile 応答に Cache-Control ヘッダを付ける。asOf あり時は no-store。
public sealed class TileFileResult : IResult
{
    private readonly byte[] _pngBytes;
    private readonly bool _noStore;

    public TileFileResult(byte[] pngBytes, bool noStore = false)
    {
        _pngBytes = pngBytes;
        _noStore = noStore;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "image/png";
        // D'102 (WD'1): cache busting で URL に ?sv= が入る前提で max-age を 24h + immutable に強化
        httpContext.Response.Headers.CacheControl = _noStore
            ? "no-store, no-cache, must-revalidate"
            : "max-age=86400, public, immutable";
        httpContext.Response.ContentLength = _pngBytes.Length;
        await httpContext.Response.Body.WriteAsync(_pngBytes);
    }
}
