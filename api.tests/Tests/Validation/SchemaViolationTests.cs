using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Validation;

[Collection(PostgisCollection.Name)]
public sealed class SchemaViolationTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public SchemaViolationTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_WithoutRequired_Returns422_WithRequiredError()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();

        var body = new
        {
            layerId = 2,                                   // 観測点（name 必須）
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object>()  // name 欠落
        };

        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);

        var errs = await ExtractErrorsAsync(res);
        var nameErr = Assert.Single(errs);
        Assert.Equal("name", nameErr.GetProperty("attributeKey").GetString());
        Assert.Equal("required", nameErr.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Post_WithWrongType_Returns422_WithTypeMismatchError()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActor("alice").Build();

        // schema: name=string required。数値を渡して type_mismatch を期待
        var body = new
        {
            layerId = 2,
            geometry = JsonDocument.Parse(@"{""type"":""Point"",""coordinates"":[143.2,42.91]}").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = 123 }
        };

        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);

        var errs = await ExtractErrorsAsync(res);
        var nameErr = Assert.Single(errs);
        Assert.Equal("name", nameErr.GetProperty("attributeKey").GetString());
        Assert.Equal("type_mismatch", nameErr.GetProperty("code").GetString());
    }

    private static async Task<JsonElement[]> ExtractErrorsAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // top-level errors または extensions.errors を吸収
        JsonElement errs;
        if (root.TryGetProperty("errors", out var topErrs))
        {
            errs = topErrs;
        }
        else
        {
            Assert.True(root.TryGetProperty("extensions", out var ext));
            Assert.True(ext.TryGetProperty("errors", out errs));
        }
        return errs.EnumerateArray().Select(e => e.Clone()).ToArray();
    }
}
