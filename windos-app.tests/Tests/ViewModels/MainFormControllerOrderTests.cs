using System.Text.Json;
using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// F'306 (Phase F' WF'3): MainFormController の OrderedLayerIds + ReorderLayers + 永続化 経路を検証。
// 3 ケース:
//   1) SetLayerVisible で OrderedLayerIds が同期する (追加は末尾、削除は除去)
//   2) ReorderLayers で z-order が変わる + 不整合は例外
//   3) ApplyPersistedLayerOrder で永続化された順序を適用
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
    public async Task ReorderLayers_ChangesZOrder_AndRejectsMismatch()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(2, true);
        ctrl.SetLayerVisible(3, true);

        // 正常: 順序変更
        ctrl.ReorderLayers(new[] { 3, 1, 2 });
        Assert.Equal(new[] { 3, 1, 2 }, ctrl.OrderedLayerIds);

        // 異常: 要素不一致
        Assert.Throws<InvalidOperationException>(() =>
            ctrl.ReorderLayers(new[] { 1, 2 })); // count mismatch
        Assert.Throws<InvalidOperationException>(() =>
            ctrl.ReorderLayers(new[] { 1, 2, 999 })); // unknown
    }

    [Fact]
    public async Task ApplyPersistedLayerOrder_AppliesIntersectionInOrder()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);
        ctrl.SetLayerVisible(2, true);
        ctrl.SetLayerVisible(3, true);
        // 現在 visible = {1, 2, 3} order=[1,2,3]

        // 永続化: [3, 1, 999] (999 は存在しない、2 は欠落)
        ctrl.ApplyPersistedLayerOrder(new[] { 3, 1, 999 });

        // 期待: 元 visible × persisted の積集合を persisted 順で → [3, 1]、
        //       元 visible だが persisted に無い 2 は末尾に追加 → [3, 1, 2]
        Assert.Equal(new[] { 3, 1, 2 }, ctrl.OrderedLayerIds);
    }
}
