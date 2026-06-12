using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// F'306 → LG305 (Phase LG WLG3): z-order 経路を LayerTreeModel ベースに追従。
//   OrderedLayerIds は「可視レイヤの DFS 列挙」になり、並べ替えは ReorderLayers では
//   なく MoveLayer (ツリー内移動) で行う。
// 3 ケース:
//   1) SetLayerVisible で OrderedLayerIds が同期する (順序はツリーの DFS 位置)
//   2) MoveLayer で z-order が変わる + 未知 id は例外
//   3) 非表示レイヤは OrderedLayerIds に現れない (ツリー位置は保持)
public sealed class MainFormControllerOrderTests
{
    private static LayerDto MakeLayer(int id, bool canEdit = false) =>
        new(LayerId: id, LayerName: $"L{id}", LayerType: "polygon",
            OwnerOrgId: null, IsShared: false,
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: 1,
            Schema: new LayerSchemaDto(Array.Empty<SchemaFieldDto>()),
            CanEdit: canEdit);

    private static MainFormController NewController(FakeApiClient api)
        => new(api, new InMemorySessionStore(), new AsOfState());

    [Fact]
    public async Task SetLayerVisible_KeepsOrderedLayerIdsSynced()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        // 初回 reload で先頭 1 が ON

        ctrl.SetLayerVisible(2, true);
        ctrl.SetLayerVisible(3, true);
        Assert.Equal(new[] { 1, 2, 3 }, ctrl.OrderedLayerIds);

        ctrl.SetLayerVisible(2, false);
        Assert.Equal(new[] { 1, 3 }, ctrl.OrderedLayerIds);
        Assert.DoesNotContain(2, ctrl.VisibleLayerIds);
    }

    [Fact]
    public async Task MoveLayer_ChangesZOrder_AndRejectsUnknownId()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(2, true);
        ctrl.SetLayerVisible(3, true);

        // 正常: 3 をルート先頭へ移動 → DFS = [3, 1, 2]
        ctrl.MoveLayer(3, parentKey: null, order: 0);
        Assert.Equal(new[] { 3, 1, 2 }, ctrl.OrderedLayerIds);

        // 異常: 未知 layerId
        Assert.Throws<KeyNotFoundException>(() => ctrl.MoveLayer(999, null, 0));
    }

    [Fact]
    public async Task HiddenLayer_KeepsTreePosition_ButNotInOrderedIds()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(2, true);
        ctrl.SetLayerVisible(3, true);
        ctrl.SetLayerVisible(2, false);

        // 非表示の 2 はツリーには残る (再 ON で同じ位置に戻る) が z-order からは除外
        Assert.Equal(new[] { 1, 2, 3 }, ctrl.Tree.EnumerateAllLayerIds());
        Assert.Equal(new[] { 1, 3 }, ctrl.OrderedLayerIds);

        ctrl.SetLayerVisible(2, true);
        Assert.Equal(new[] { 1, 2, 3 }, ctrl.OrderedLayerIds);
    }
}
