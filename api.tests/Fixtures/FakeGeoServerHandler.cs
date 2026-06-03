using System.Net;
using System.Net.Http.Headers;

namespace AgriGis.Api.Tests.Fixtures;

// E'302 (WE'3): TilesEndpoints テスト用の GeoServer モック handler。
// WMS GetMap リクエストに対して 1x1 透過 PNG を返す。
public sealed class FakeGeoServerHandler : HttpMessageHandler
{
    // 1x1 透過 PNG (89 bytes)
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(OnePixelPng)
        };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return Task.FromResult(resp);
    }
}
