using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriGis.Api.Dto;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.UserPreference;

// F'306 (Phase F' WF'3): /api/user/preferences/{key} GET/PUT を検証。
[Collection(PostgisCollection.Name)]
public sealed class UserPreferenceEndpointTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    public UserPreferenceEndpointTests(PostgisContainerFixture pg) => _pg = pg;
    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_NotSet_Returns404()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var res = await client.GetAsync("/api/user/preferences/layer_order_v1");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Put_ThenGet_ReturnsSameValue()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var body = new { value = new[] { 5, 2, 1 } };
        var putRes = await client.PutAsJsonAsync("/api/user/preferences/layer_order_v1", body);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        var getRes = await client.GetAsync("/api/user/preferences/layer_order_v1");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var got = await getRes.Content.ReadFromJsonAsync<UserPreferenceDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(got);
        Assert.Equal("layer_order_v1", got!.Key);
        Assert.Equal(JsonValueKind.Array, got.Value.ValueKind);
        var arr = got.Value.EnumerateArray().Select(e => e.GetInt32()).ToList();
        Assert.Equal(new[] { 5, 2, 1 }, arr);
    }

    [Fact]
    public async Task Put_InvalidKeyFormat_Returns422()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var client = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();

        var body = new { value = new[] { 1 } };
        // 不正キー: コロン含有
        var res = await client.PutAsJsonAsync("/api/user/preferences/bad%3Akey", body);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
    }

    [Fact]
    public async Task Get_IsolatedPerUser()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var aliceClient = new ApiClientFactory(api).WithActorAs(SeedUsers.AliceLogin, "admin").Build();
        var bobClient = new ApiClientFactory(api).WithActorAs(SeedUsers.BobLogin, "general").Build();

        // Alice が保存
        await aliceClient.PutAsJsonAsync("/api/user/preferences/layer_order_v1",
            new { value = new[] { 1, 2 } });

        // Bob は同じキーで 404 (他人の設定にアクセス不可)
        var bobRes = await bobClient.GetAsync("/api/user/preferences/layer_order_v1");
        Assert.Equal(HttpStatusCode.NotFound, bobRes.StatusCode);
    }
}
