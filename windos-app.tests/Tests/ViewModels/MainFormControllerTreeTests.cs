using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core.LayerTree;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// LG305 (Phase LG WLG3): MainFormController のツリー配線を検証する。
// - ReloadAsync の defaultTree 構築 (GET /api/layer-groups + LayerDto.GroupId/SortOrder)
// - layer_tree_v1 preference との Merge 配線
// - OrderedLayerIds = 可視レイヤの DFS (z-order)
// - layer_flags_v1 の往復 (SaveFlagsAsync / 適用)
// - 旧 layer_order_v1 からの初回移行
// - usr: グループ作成/改名/削除 + layer_tree_v1 往復
public sealed class MainFormControllerTreeTests
{
    private static LayerDto MakeLayer(int id, int? groupId = null, int sortOrder = 0) =>
        new(LayerId: id, LayerName: $"L{id}", LayerType: "polygon",
            OwnerOrgId: null, IsShared: false,
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: 1,
            Schema: new LayerSchemaDto(Array.Empty<SchemaFieldDto>()),
            CanEdit: false,
            GroupId: groupId,
            SortOrder: sortOrder);

    private static MainFormController NewController(FakeApiClient api)
        => new(api, new InMemorySessionStore(), new AsOfState());

    private static void SeedPreference(FakeApiClient api, string key, string json)
        => api.Preferences[key] = new UserPreferenceDto(
            key, JsonSerializer.Deserialize<JsonElement>(json), DateTimeOffset.UtcNow);

    // ---------------- defaultTree 構築 ----------------

    [Fact]
    public async Task Reload_NoGroupsNoPreference_BuildsFlatTree_FirstLayerOn()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, ctrl.Tree.EnumerateAllLayerIds());
        Assert.Empty(ctrl.Tree.EnumerateGroups());
        // 既存挙動維持: 先頭 layer が初期 ON
        Assert.Equal(new[] { 1 }, ctrl.OrderedLayerIds);
    }

    [Fact]
    public async Task Reload_BuildsDefaultTree_FromGroupsAndLayerPlacement()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[]
            {
                MakeLayer(10, groupId: 1, sortOrder: 0),
                MakeLayer(11, groupId: 1, sortOrder: 1),
                MakeLayer(30, groupId: null, sortOrder: 5),
            },
            LayerGroups =
            {
                new LayerGroupDto(1, null, "賦課", 0),
                new LayerGroupDto(2, 1, "賦課詳細", 1),
            },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        var g1 = ctrl.Tree.FindGroup("db:1");
        Assert.NotNull(g1);
        Assert.Equal("賦課", g1!.Name);
        Assert.Equal("db:1", ctrl.Tree.FindGroup("db:2")!.ParentKey);
        Assert.Equal("db:1", ctrl.Tree.FindLayer(10)!.ParentKey);
        Assert.Equal("db:1", ctrl.Tree.FindLayer(11)!.ParentKey);
        Assert.Null(ctrl.Tree.FindLayer(30)!.ParentKey);
    }

    [Fact]
    public async Task Reload_MergesTreePreference_StructureFromPref_GroupNameFromDb()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[]
            {
                MakeLayer(10, groupId: 1),
                MakeLayer(11, groupId: 1, sortOrder: 1),
            },
            LayerGroups = { new LayerGroupDto(1, null, "賦課", 0) },
        };
        // preference: 11 と 10 を逆順 + db:1 の名前は古い
        SeedPreference(api, LayerTreeModel.PreferenceKey, """
            {
              "groups": [{ "key": "db:1", "name": "古い名前", "parent": null, "order": 0, "expanded": true }],
              "layers": [
                { "layerId": 11, "parent": "db:1", "order": 0 },
                { "layerId": 10, "parent": "db:1", "order": 1 }
              ]
            }
            """);
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        // 構造は preference、グループ名は DB 優先
        Assert.Equal(new[] { 11, 10 }, ctrl.Tree.EnumerateAllLayerIds());
        Assert.Equal("賦課", ctrl.Tree.FindGroup("db:1")!.Name);
    }

    // ---------------- OrderedLayerIds = DFS ----------------

    [Fact]
    public async Task OrderedLayerIds_IsDfsOfVisibleLayers()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[]
            {
                MakeLayer(10, groupId: 1, sortOrder: 1),
                MakeLayer(20, groupId: 2, sortOrder: 0),
                MakeLayer(30, groupId: null, sortOrder: 9),
            },
            LayerGroups =
            {
                new LayerGroupDto(1, null, "親", 0),
                new LayerGroupDto(2, 1, "子", 0),
            },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(10, true);
        ctrl.SetLayerVisible(20, true);
        ctrl.SetLayerVisible(30, true);

        // DFS (上から): db:1 → db:2 → 20, 10, 30
        Assert.Equal(new[] { 20, 10, 30 }, ctrl.OrderedLayerIds);
    }

    // ---------------- グループ一括 ON/OFF + 3 値 ----------------

    [Fact]
    public async Task SetGroupVisible_TogglesAllDescendantLayers()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[]
            {
                MakeLayer(10, groupId: 1),
                MakeLayer(11, groupId: 1, sortOrder: 1),
                MakeLayer(30),
            },
            LayerGroups = { new LayerGroupDto(1, null, "賦課", 0) },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        // 初期 ON は先頭 (10) → Mixed
        Assert.Equal(GroupCheckState.Mixed, ctrl.GetGroupCheckState("db:1"));

        ctrl.SetGroupVisible("db:1", true);
        Assert.Equal(GroupCheckState.Checked, ctrl.GetGroupCheckState("db:1"));
        Assert.Equal(new[] { 10, 11 }, ctrl.OrderedLayerIds);

        ctrl.SetGroupVisible("db:1", false);
        Assert.Equal(GroupCheckState.Unchecked, ctrl.GetGroupCheckState("db:1"));
        Assert.Empty(ctrl.OrderedLayerIds);
    }

    // ---------------- 移動 + layer_tree_v1 往復 ----------------

    [Fact]
    public async Task MoveLayer_IntoGroup_PersistsAndRestoresViaTreePreference()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2, groupId: 1) },
            LayerGroups = { new LayerGroupDto(1, null, "G", 0) },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        Assert.Null(ctrl.Tree.FindLayer(1)!.ParentKey);

        ctrl.MoveLayer(1, "db:1", 0);
        await ctrl.SaveTreeAsync(CancellationToken.None);
        Assert.True(api.Preferences.ContainsKey(LayerTreeModel.PreferenceKey));

        // 別 controller (再起動相当) で復元
        var ctrl2 = NewController(api);
        await ctrl2.ReloadAsync(null, CancellationToken.None);
        Assert.Equal("db:1", ctrl2.Tree.FindLayer(1)!.ParentKey);
        Assert.Equal(new[] { 1, 2 }, ctrl2.Tree.EnumerateAllLayerIds());
    }

    [Fact]
    public async Task MoveGroup_IntoOwnDescendant_Throws()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1, groupId: 2) },
            LayerGroups =
            {
                new LayerGroupDto(1, null, "親", 0),
                new LayerGroupDto(2, 1, "子", 0),
            },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => ctrl.MoveGroup("db:1", "db:2", 0));
        Assert.Throws<InvalidOperationException>(() => ctrl.MoveGroup("db:1", "db:1", 0));
    }

    // ---------------- usr: グループ ----------------

    [Fact]
    public async Task CreateUserGroup_MoveLayerIn_RoundTripsThroughPreference()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        var key = ctrl.CreateUserGroup("自分用", null);
        Assert.StartsWith("usr:", key);
        ctrl.MoveLayer(1, key, 0);
        await ctrl.SaveTreeAsync(CancellationToken.None);

        var ctrl2 = NewController(api);
        await ctrl2.ReloadAsync(null, CancellationToken.None);
        Assert.Equal(key, ctrl2.Tree.FindLayer(1)!.ParentKey);
        Assert.Equal("自分用", ctrl2.Tree.FindGroup(key)!.Name);
    }

    [Fact]
    public async Task RemoveGroup_EvacuatesLayersToParent()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        var key = ctrl.CreateUserGroup("一時", null);
        ctrl.MoveLayer(1, key, 0);

        ctrl.RemoveGroup(key);

        Assert.Null(ctrl.Tree.FindGroup(key));
        Assert.Null(ctrl.Tree.FindLayer(1)!.ParentKey);
        Assert.Equal(new[] { 2, 1 }, ctrl.Tree.EnumerateAllLayerIds());
    }

    [Fact]
    public async Task RenameUserGroup_RenamesUsrOnly_IgnoresDbGroups()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1, groupId: 1) },
            LayerGroups = { new LayerGroupDto(1, null, "賦課", 0) },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        var usrKey = ctrl.CreateUserGroup("旧名", null);

        ctrl.RenameUserGroup(usrKey, "新名");
        Assert.Equal("新名", ctrl.Tree.FindGroup(usrKey)!.Name);

        // db: グループは DB 側が正なので無視される
        ctrl.RenameUserGroup("db:1", "勝手な名前");
        Assert.Equal("賦課", ctrl.Tree.FindGroup("db:1")!.Name);
    }

    // ---------------- layer_flags_v1 往復 ----------------

    [Fact]
    public async Task SaveFlags_RoundTripsThroughPreference()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        ctrl.SetLayerFlags(1, edit: true);
        ctrl.SetLayerFlags(2, snap: true);
        await ctrl.SaveFlagsAsync(CancellationToken.None);
        Assert.True(api.Preferences.ContainsKey(MainFormController.FlagsPreferenceKey));

        var ctrl2 = NewController(api);
        await ctrl2.ReloadAsync(null, CancellationToken.None);
        var l1 = ctrl2.Tree.FindLayer(1)!;
        var l2 = ctrl2.Tree.FindLayer(2)!;
        Assert.True(l1.EditEnabled);
        Assert.False(l1.SnapEnabled);
        Assert.False(l2.EditEnabled);
        Assert.True(l2.SnapEnabled);
    }

    [Fact]
    public async Task FlagsPreference_UnknownLayerIdAndBrokenValues_IgnoredGracefully()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1) }
        };
        SeedPreference(api, MainFormController.FlagsPreferenceKey, """
            {
              "999": { "edit": true, "snap": true },
              "abc": { "edit": true },
              "1": { "edit": "broken", "snap": true }
            }
            """);
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        var l1 = ctrl.Tree.FindLayer(1)!;
        Assert.False(l1.EditEnabled); // 不正値は無視
        Assert.True(l1.SnapEnabled);  // 正常値のみ適用
    }

    // ---------------- 旧 layer_order_v1 移行 ----------------

    [Fact]
    public async Task Migration_FromLayerOrderV1_SetsVisibilityAndRelativeOrder()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        SeedPreference(api, MainFormController.LayerOrderPreferenceKey, "[3, 1]");
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        // flatOrder 中のレイヤのみ visible、相対順序は flatOrder 準拠 (先頭 ON は発動しない)
        Assert.Equal(new[] { 3, 1 }, ctrl.OrderedLayerIds);
        Assert.False(ctrl.Tree.FindLayer(2)!.Visible);
    }

    [Fact]
    public async Task Migration_SkippedWhenTreePreferenceExists()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        // 両方ある場合は layer_tree_v1 が勝つ (layer_order_v1 は無視)
        SeedPreference(api, MainFormController.LayerOrderPreferenceKey, "[3, 1]");
        SeedPreference(api, LayerTreeModel.PreferenceKey, """
            {
              "groups": [],
              "layers": [
                { "layerId": 2, "parent": null, "order": 0 },
                { "layerId": 1, "parent": null, "order": 1 },
                { "layerId": 3, "parent": null, "order": 2 }
              ]
            }
            """);
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.Equal(new[] { 2, 1, 3 }, ctrl.Tree.EnumerateAllLayerIds());
        // visible はセッション状態 (pref には含まれない) → 先頭 layer 初期 ON
        Assert.Equal(new[] { 1 }, ctrl.OrderedLayerIds);
    }

    // ---------------- 再 reload ----------------

    [Fact]
    public async Task SecondReload_PreservesSessionVisibility_AndDropsDeletedLayers()
    {
        var calls = 0;
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => (++calls == 1)
                ? new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
                : new[] { MakeLayer(2), MakeLayer(3), MakeLayer(4) }, // 1 削除 + 4 追加
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(2, true); // visible = {1, 2}

        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.DoesNotContain(1, ctrl.VisibleLayerIds);
        Assert.Contains(2, ctrl.VisibleLayerIds);
        Assert.DoesNotContain(4, ctrl.VisibleLayerIds); // 新規 layer は OFF で出現
        Assert.Contains(4, ctrl.Tree.EnumerateAllLayerIds());
    }

    [Fact]
    public async Task Reload_NewLayerAfterPreferenceSaved_AppearsAtDefaultPosition()
    {
        var calls = 0;
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => (++calls <= 2)
                ? new[] { MakeLayer(10, groupId: 1) }
                : new[] { MakeLayer(10, groupId: 1), MakeLayer(11, groupId: 1, sortOrder: 1) },
            LayerGroups = { new LayerGroupDto(1, null, "賦課", 0) },
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);   // calls=1
        await ctrl.SaveTreeAsync(CancellationToken.None);        // pref には layer 10 のみ
        await ctrl.ReloadAsync(null, CancellationToken.None);   // calls=2 (pref 経由)

        await ctrl.ReloadAsync(null, CancellationToken.None);   // calls=3: 新 layer 11 出現

        // preference に無い新規 layer は defaultTree の位置 (db:1 配下) へ
        Assert.Equal("db:1", ctrl.Tree.FindLayer(11)!.ParentKey);
    }
}
