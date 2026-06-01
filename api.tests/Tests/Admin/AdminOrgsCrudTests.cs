using System.Net;
using System.Net.Http.Json;
using AgriGis.Api.Tests.Fixtures;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Admin;

// A506: /api/admin/organizations CRUD のハッピーパス + 論理削除後の code 再利用
[Collection(PostgisCollection.Name)]
public sealed class AdminOrgsCrudTests : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;

    public AdminOrgsCrudTests(PostgisContainerFixture pg) => _pg = pg;

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Crud_HappyPath()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var created = await admin.PostAsJsonAsync("/api/admin/organizations",
            new { name = "新規 Org", code = "new-org" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<OrgRes>();
        Assert.NotNull(dto);
        Assert.True(dto!.Id > 0);

        var listRes = await admin.GetAsync("/api/admin/organizations");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<List<OrgRes>>();
        Assert.Contains(list!, o => o.Code == "new-org");

        var patchRes = await admin.PatchAsJsonAsync($"/api/admin/organizations/{dto.Id}",
            new { name = "改名" });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var updated = await patchRes.Content.ReadFromJsonAsync<OrgRes>();
        Assert.Equal("改名", updated!.Name);

        var delRes = await admin.DeleteAsync($"/api/admin/organizations/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var listAfter = await admin.GetAsync("/api/admin/organizations");
        var afterList = await listAfter.Content.ReadFromJsonAsync<List<OrgRes>>();
        Assert.DoesNotContain(afterList!, o => o.Id == dto.Id);
    }

    [Fact]
    public async Task LogicalDelete_AllowsCodeReuse()
    {
        await using var api = new ApiFactory(_pg.ConnectionString);
        var admin = new ApiClientFactory(api).WithActorAs("alice", "admin").Build();

        var c1 = await admin.PostAsJsonAsync("/api/admin/organizations",
            new { name = "Org A", code = "shared-code" });
        Assert.Equal(HttpStatusCode.Created, c1.StatusCode);
        var dto1 = await c1.Content.ReadFromJsonAsync<OrgRes>();

        await admin.DeleteAsync($"/api/admin/organizations/{dto1!.Id}");

        var c2 = await admin.PostAsJsonAsync("/api/admin/organizations",
            new { name = "Org A 再", code = "shared-code" });
        Assert.Equal(HttpStatusCode.Created, c2.StatusCode);
    }

    private sealed record OrgRes(int Id, string Name, string Code, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
