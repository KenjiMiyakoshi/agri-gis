using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using AgriGis.Api.Options;
using AgriGis.Api.Tiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// D202 (WD2):
//   POST   /api/selection                                 (admin/general)
//   GET    /tiles/selection/{sid}/{z}/{x}/{y}.png         (Bearer + owner)
//   DELETE /api/selection/{sid}                           (Bearer + owner)
public static class SelectionEndpoints
{
    public const int MaxEntityIds = 50_000;
    private static readonly Regex ColorHexRegex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static RouteGroupBuilder MapSelectionEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/selection
        group.MapPost("/", async (CreateSelectionRequestDto req,
                                  NpgsqlDataSource db,
                                  ICurrentUser user,
                                  CancellationToken ct) =>
        {
            if (!user.HasRole("admin") && !user.HasRole("general"))
            {
                return Results.Forbid();
            }
            if (req.EntityIds is null || req.EntityIds.Count == 0)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("entityIds", "required", "entityIds must be a non-empty array")
                });
            }
            if (req.EntityIds.Count > MaxEntityIds)
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("entityIds", "maxItems",
                        $"entityIds count {req.EntityIds.Count} exceeds maximum {MaxEntityIds}")
                });
            }
            var colorHex = req.ColorHex ?? "#FFEB3B";
            if (!ColorHexRegex.IsMatch(colorHex))
            {
                throw new ValidationException(new[]
                {
                    new AttributeErrorDto("colorHex", "pattern", "colorHex must match ^#[0-9A-Fa-f]{6}$")
                });
            }

            const string sql = @"
                INSERT INTO selection_sets (user_id, session_id, entity_ids, color_hex)
                VALUES (@u, @s, @e, @c)
                RETURNING sid";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("u", user.UserId);
            cmd.Parameters.AddWithValue("s", user.SessionId);
            cmd.Parameters.AddWithValue("e", req.EntityIds.ToArray());
            cmd.Parameters.AddWithValue("c", colorHex);
            var scalarResult = await cmd.ExecuteScalarAsync(ct);
            if (scalarResult is not Guid sid)
            {
                throw new InvalidOperationException("INSERT did not return sid");
            }

            return Results.Created($"/api/selection/{sid}",
                new CreateSelectionResponseDto(sid, "session", req.EntityIds.Count));
        });

        // DELETE /api/selection/{sid}
        group.MapDelete("/{sid:guid}", async (Guid sid,
                                              NpgsqlDataSource db,
                                              ICurrentUser user,
                                              CancellationToken ct) =>
        {
            const string sql = @"
                DELETE FROM selection_sets
                 WHERE sid = @s AND user_id = @u";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("s", sid);
            cmd.Parameters.AddWithValue("u", user.UserId);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            // owner 不一致 / 既に削除 → どちらも 204 で冪等
            // (404 を返すと存在確認の oracle になり、列挙攻撃の余地)
            return Results.NoContent();
        });

        return group;
    }

    // GET /tiles/selection/{sid}/{z}/{x}/{y}.png は別グループ (/tiles) に MapTilesSelectionEndpoint で配線
    public static RouteGroupBuilder MapTilesSelectionEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/selection/{sid:guid}/{z:int}/{x:int}/{y:int}.png",
            async (Guid sid, int z, int x, int y,
                   NpgsqlDataSource db,
                   ICurrentUser user,
                   IHttpClientFactory httpClientFactory,
                   IOptions<GeoServerOptions> geoOpts,
                   CancellationToken ct) =>
        {
            // 1) sid → user_id 比較
            const string lookupSql = @"
                SELECT user_id, entity_ids, color_hex
                  FROM selection_sets
                 WHERE sid = @s";
            await using (var cmd = db.CreateCommand(lookupSql))
            {
                cmd.Parameters.AddWithValue("s", sid);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                {
                    return Results.NotFound();
                }
                var ownerId = r.GetGuid(0);
                if (ownerId != user.UserId)
                {
                    return Results.Forbid();
                }
                var entityIds = (Guid[])r.GetValue(1);
                var colorHex = r.GetString(2);
                // 接続を close してから GeoServer に proxy するため変数化
                return await ProxyToGeoServerWithCqlFilter(
                    entityIds, colorHex, z, x, y, geoOpts.Value, httpClientFactory, ct);
            }
        });

        return group;
    }

    private static async Task<IResult> ProxyToGeoServerWithCqlFilter(
        Guid[] entityIds, string colorHex, int z, int x, int y,
        GeoServerOptions opts, IHttpClientFactory factory, CancellationToken ct)
    {
        (double minX, double minY, double maxX, double maxY) bbox;
        try
        {
            bbox = WebMercatorTileMath.TileToBbox3857(z, x, y);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { title = "Invalid z/x/y", detail = ex.Message });
        }

        // CQL_FILTER で entity_id IN (...) を組み立てる
        var cql = "entity_id IN (" + string.Join(",", entityIds.Select(e => $"'{e}'")) + ")";

        // GeoServer に GetMap で selection 専用 SLD (selection_overlay) + CQL_FILTER 投げ
        // (selection_overlay SLD は WD0/WD1 で data_dir/styles に配置、color_hex は ENV パラメタで渡す案)
        var url = $"{opts.BaseUrl.TrimEnd('/')}/{opts.Workspace}/wms" +
                  $"?service=WMS&version=1.1.1&request=GetMap" +
                  $"&layers={opts.Workspace}:selection_layer" +
                  $"&styles={opts.Workspace}:selection_overlay" +
                  $"&bbox={WebMercatorTileMath.FormatBboxArg(bbox.minX, bbox.minY, bbox.maxX, bbox.maxY)}" +
                  $"&width=256&height=256&srs=EPSG:3857&format=image/png&transparent=true" +
                  $"&CQL_FILTER={Uri.EscapeDataString(cql)}" +
                  $"&env=" + Uri.EscapeDataString($"highlight:{colorHex}");

        var client = factory.CreateClient("geoserver");
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
            return Results.Problem(title: "GeoServer unreachable", detail: ex.Message, statusCode: 503);
        }

        if (!geoResp.IsSuccessStatusCode)
        {
            var bodyText = await geoResp.Content.ReadAsStringAsync(ct);
            return Results.Problem(title: "GeoServer returned error",
                detail: $"status={(int)geoResp.StatusCode}; body={bodyText[..Math.Min(500, bodyText.Length)]}",
                statusCode: 502);
        }

        var pngBytes = await geoResp.Content.ReadAsByteArrayAsync(ct);
        // selection overlay はキャッシュ不要 (短命 + ユーザ固有)
        return new SelectionTileResult(pngBytes);
    }
}

// D202: selection overlay 応答にキャッシュ抑制ヘッダを付ける
public sealed class SelectionTileResult : IResult
{
    private readonly byte[] _pngBytes;

    public SelectionTileResult(byte[] pngBytes) => _pngBytes = pngBytes;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "image/png";
        httpContext.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        httpContext.Response.ContentLength = _pngBytes.Length;
        await httpContext.Response.Body.WriteAsync(_pngBytes);
    }
}
