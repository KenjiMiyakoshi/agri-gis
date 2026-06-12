using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// F307 (Phase F WF3): OrgPermissionsViewModel の検証。
// 5 ケース:
//   1) LoadOrgsAsync が組織一覧を取得して Orgs に格納
//   2) LoadPermissionsAsync が指定 org の権限を Permissions に格納
//   3) SetCanEdit(true) が canView も auto-ON にする (CHECK 制約)
//   4) SetCanView(false) が canEdit も auto-OFF にする (CHECK 制約)
//   5) SaveAsync が PUT を 1 回呼び、リクエストに変更後の値が含まれる
public sealed class OrgPermissionsViewModelTests
{
    private static OrgDto MakeOrg(int id, string code = "c") =>
        new(Id: id, Name: $"Org{id}", Code: code,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

    private static OrgLayerPermissionDto MakePerm(int orgId, int layerId, bool canView, bool canEdit) =>
        new(orgId, layerId, $"L{layerId}", "polygon", canView, canEdit);

    [Fact]
    public async Task LoadOrgsAsync_PopulatesOrgs()
    {
        var api = new FakeApiClient
        {
            Orgs = { MakeOrg(1), MakeOrg(2) }
        };
        var vm = new OrgPermissionsViewModel(api);

        await vm.LoadOrgsAsync(CancellationToken.None);

        Assert.Equal(2, vm.Orgs.Count);
        Assert.Equal(1, vm.Orgs[0].Id);
    }

    [Fact]
    public async Task LoadPermissionsAsync_PopulatesPermissions()
    {
        var api = new FakeApiClient();
        api.PermsByOrg[1] = new()
        {
            MakePerm(1, 10, canView: true, canEdit: false),
            MakePerm(1, 20, canView: false, canEdit: false)
        };
        var vm = new OrgPermissionsViewModel(api);

        await vm.LoadPermissionsAsync(1, CancellationToken.None);

        Assert.Equal(1, vm.SelectedOrgId);
        Assert.Equal(2, vm.Permissions.Count);
        Assert.True(vm.GetPermission(10)!.CanView);
    }

    [Fact]
    public async Task SetCanEdit_True_AlsoEnablesCanView()
    {
        var api = new FakeApiClient();
        api.PermsByOrg[1] = new()
        {
            MakePerm(1, 10, canView: false, canEdit: false)
        };
        var vm = new OrgPermissionsViewModel(api);
        await vm.LoadPermissionsAsync(1, CancellationToken.None);

        vm.SetCanEdit(10, true);

        var p = vm.GetPermission(10);
        Assert.NotNull(p);
        Assert.True(p!.CanEdit);
        Assert.True(p.CanView); // auto-ON
    }

    [Fact]
    public async Task SetCanView_False_AlsoDisablesCanEdit()
    {
        var api = new FakeApiClient();
        api.PermsByOrg[1] = new()
        {
            MakePerm(1, 10, canView: true, canEdit: true)
        };
        var vm = new OrgPermissionsViewModel(api);
        await vm.LoadPermissionsAsync(1, CancellationToken.None);

        vm.SetCanView(10, false);

        var p = vm.GetPermission(10);
        Assert.NotNull(p);
        Assert.False(p!.CanView);
        Assert.False(p.CanEdit); // auto-OFF
    }

    [Fact]
    public async Task SaveAsync_CallsUpdateWithCurrentState()
    {
        var api = new FakeApiClient();
        api.PermsByOrg[1] = new()
        {
            MakePerm(1, 10, canView: false, canEdit: false),
            MakePerm(1, 20, canView: true, canEdit: false)
        };
        var vm = new OrgPermissionsViewModel(api);
        await vm.LoadPermissionsAsync(1, CancellationToken.None);
        vm.SetCanEdit(10, true);   // layer 10: edit + view ON
        vm.SetCanView(20, false);  // layer 20: view + edit OFF

        await vm.SaveAsync(CancellationToken.None);

        Assert.Equal(1, api.UpdateOrgPermsCalls);
        var req = api.LastUpdateReq;
        Assert.NotNull(req);
        var item10 = req!.Permissions.Single(p => p.LayerId == 10);
        Assert.True(item10.CanView);
        Assert.True(item10.CanEdit);
        var item20 = req.Permissions.Single(p => p.LayerId == 20);
        Assert.False(item20.CanView);
        Assert.False(item20.CanEdit);
    }
}
