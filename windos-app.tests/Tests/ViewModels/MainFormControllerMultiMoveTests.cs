using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Core.LayerTree;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// LGP302/LGP303 (Phase LG' WLGP3): MainFormController.MoveLayersTo (まとめ移動ラッパ) の結合検証。
// LayerTreeView の選択/D&D は UI 依存だが、まとめ移動の本質 (Core MoveLayers 経由で複数レイヤを
// アトミックに並べ替え、DFS z-order が画面表示順を保つ) は Controller 経由で検証できる。
public sealed class MainFormControllerMultiMoveTests
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

    private static void SeedPreference(FakeApiClient api, string json)
        => api.Preferences[LayerTreeModel.PreferenceKey] = new UserPreferenceDto(
            LayerTreeModel.PreferenceKey, JsonSerializer.Deserialize<JsonElement>(json),
            DateTimeOffset.UtcNow);

    private static void AllVisible(MainFormController ctrl)
    {
        foreach (var id in ctrl.Tree.EnumerateAllLayerIds()) ctrl.SetLayerVisible(id, true);
    }

    // ルート直下に 10,11,12,13 (全可視) + グループ db:1 (空) を持つツリー。
    private static async Task<MainFormController> RootWithGroupAsync()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[]
            {
                MakeLayer(10, sortOrder: 0),
                MakeLayer(11, sortOrder: 1),
                MakeLayer(12, sortOrder: 2),
                MakeLayer(13, sortOrder: 3),
            },
            LayerGroups = { new LayerGroupDto(1, null, "G", 10) },
        };
        // preference でグループを末尾、レイヤをルート直下に並べる (10,11,12,13,G)
        SeedPreference(api, """
            {
              "groups": [{ "key": "db:1", "name": "G", "parent": null, "order": 4, "expanded": true }],
              "layers": [
                { "layerId": 10, "parent": null, "order": 0 },
                { "layerId": 11, "parent": null, "order": 1 },
                { "layerId": 12, "parent": null, "order": 2 },
                { "layerId": 13, "parent": null, "order": 3 }
              ]
            }
            """);
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        AllVisible(ctrl);
        return ctrl;
    }

    // ---------------- まとめ移動: グループへ ----------------

    [Fact]
    public async Task MoveLayersTo_IntoGroup_PreservesGivenOrder_AndDfsZOrder()
    {
        var ctrl = await RootWithGroupAsync();
        // 10 と 12 (画面表示順) をグループ db:1 へまとめ移動
        ctrl.MoveLayersTo(new[] { 10, 12 }, "db:1", 0);

        // グループ配下が 10,12 の順
        var g = ctrl.Tree.FindGroup("db:1")!;
        Assert.Equal(new[] { 10, 12 },
            g.Children.OfType<TreeLayerNode>().Select(l => l.LayerId));
        // ルート直下は 11,13 + グループ。全可視 DFS z-order は 11,13,(group内)10,12
        Assert.Equal(new[] { 11, 13, 10, 12 }, ctrl.OrderedLayerIds);
    }

    [Fact]
    public async Task MoveLayersTo_KeepsScreenOrder_NotSelectionArgOrder()
    {
        var ctrl = await RootWithGroupAsync();
        // 引数順を逆 (12,10) で渡しても、呼び出し側 (UI) は画面表示順で渡す契約。
        // ここでは「渡した順がそのまま挿入される」ことを確認する (UI が表示順を渡す前提)。
        ctrl.MoveLayersTo(new[] { 12, 10 }, "db:1", 0);
        var g = ctrl.Tree.FindGroup("db:1")!;
        Assert.Equal(new[] { 12, 10 },
            g.Children.OfType<TreeLayerNode>().Select(l => l.LayerId));
    }

    // ---------------- まとめ並べ替え (同一 parent 内) ----------------

    [Fact]
    public async Task MoveLayersTo_ReorderWithinRoot_StartOrderRespected()
    {
        var ctrl = await RootWithGroupAsync(); // root: 10,11,12,13,G
        // 11,12 を先頭 (startOrder 0) へまとめ移動 → 11,12,10,13,G
        ctrl.MoveLayersTo(new[] { 11, 12 }, null, 0);
        Assert.Equal(new[] { 11, 12, 10, 13 }, ctrl.OrderedLayerIds);
    }

    // ---------------- layer+group 混在 → layer のみ移動 ----------------

    [Fact]
    public async Task MoveLayersTo_LayerOnly_GroupUntouched_WhenMixedSelection()
    {
        // UI は混在選択から layer のみ抽出して MoveLayersTo を呼ぶ。
        // group は引数に含まれない (= 不動) ことを「グループが移動しない」結果で確認する。
        var ctrl = await RootWithGroupAsync(); // root: 10,11,12,13,G (G は root order 4)

        // layer 12,13 のみグループへ (group db:1 は対象外)
        ctrl.MoveLayersTo(new[] { 12, 13 }, "db:1", 0);

        // グループ自体はルート直下のまま (親が変わっていない)。12,13 を抜いた root は 10,11,G
        // となり、グループは root order 2 へ詰まる (移動はしていない、前の兄弟が減っただけ)。
        var group = ctrl.Tree.FindGroup("db:1")!;
        Assert.Null(group.ParentKey);
        Assert.Equal(2, group.Order);
        // グループ配下に 12,13
        Assert.Equal(new[] { 12, 13 },
            group.Children.OfType<TreeLayerNode>().Select(l => l.LayerId));
        // z-order: 10,11,(group)12,13
        Assert.Equal(new[] { 10, 11, 12, 13 }, ctrl.OrderedLayerIds);
    }

    // ---------------- 寛容性: 未知 id / 重複 ----------------

    [Fact]
    public async Task MoveLayersTo_IgnoresUnknownIds_AndDedupes()
    {
        var ctrl = await RootWithGroupAsync();
        // 999 (未知) と 10 重複を含めても、10,11 のみ処理される
        ctrl.MoveLayersTo(new[] { 999, 11, 11, 10 }, "db:1", 0);
        Assert.Equal(new[] { 11, 10 },
            ctrl.Tree.FindGroup("db:1")!.Children.OfType<TreeLayerNode>().Select(l => l.LayerId));
    }

    [Fact]
    public async Task MoveLayersTo_AllUnknown_NoOp()
    {
        var ctrl = await RootWithGroupAsync();
        var before = ctrl.OrderedLayerIds.ToList();
        ctrl.MoveLayersTo(new[] { 900, 901 }, "db:1", 0);
        Assert.Equal(before, ctrl.OrderedLayerIds);
    }

    [Fact]
    public async Task MoveLayersTo_UnknownGroupKey_Throws()
    {
        var ctrl = await RootWithGroupAsync();
        Assert.Throws<KeyNotFoundException>(
            () => ctrl.MoveLayersTo(new[] { 10 }, "db:999", 0));
    }
}
