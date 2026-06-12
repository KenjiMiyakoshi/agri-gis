using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// F307 (Phase F WF3): MainFormController の VisibleLayerIds + canEdit 検証。
// 5 ケース:
//   1) 初回 reload で先頭 layer が ON になる (空集合からの初期化)
//   2) SetLayerVisible で ON/OFF が反映される
//   3) reload 後も VisibleLayerIds が保持される (削除済 layer は集合から外れる)
//   4) GetLayerById が canEdit を含む LayerDto を返す
//   5) GetLayerById が存在しない id で null を返す
public sealed class MainFormControllerMultiLayerTests
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
    public async Task FirstReload_PutsFirstLayerInVisibleSet()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1), MakeLayer(2) }
        };
        var ctrl = NewController(api);

        var result = await ctrl.ReloadAsync(prevSelectedLayerId: null, CancellationToken.None);

        Assert.Equal(2, result.Layers.Count);
        Assert.Single(ctrl.VisibleLayerIds);
        Assert.Contains(1, ctrl.VisibleLayerIds);
    }

    // LG305 (Phase LG WLG3): LayerTreeModel ベースに追従。
    // 未知 layerId (ツリーに無い) の SetLayerVisible は寛容に無視されるため reload 後に検証する。
    [Fact]
    public async Task SetLayerVisible_AddsAndRemovesFromSet()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(5), MakeLayer(7) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        ctrl.SetLayerVisible(5, true);
        ctrl.SetLayerVisible(7, true);
        Assert.Contains(5, ctrl.VisibleLayerIds);
        Assert.Contains(7, ctrl.VisibleLayerIds);

        ctrl.SetLayerVisible(5, false);
        Assert.DoesNotContain(5, ctrl.VisibleLayerIds);
        Assert.Contains(7, ctrl.VisibleLayerIds);

        // ツリーに無い layerId は無視 (落ちない)
        ctrl.SetLayerVisible(999, true);
        Assert.DoesNotContain(999, ctrl.VisibleLayerIds);
    }

    [Fact]
    public async Task SecondReload_PreservesVisibleSet_ButRemovesDeletedLayers()
    {
        // 第1回 reload: 3 layers (1, 2, 3)、ユーザが 1, 2 を ON
        var calls = 0;
        var first = new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) };
        var second = new[] { MakeLayer(2), MakeLayer(3) };  // layer 1 が削除された
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => (++calls == 1) ? first : second
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        // 初回は先頭 (1) が ON、追加で 2 も ON にする
        ctrl.SetLayerVisible(2, true);
        Assert.Contains(1, ctrl.VisibleLayerIds);
        Assert.Contains(2, ctrl.VisibleLayerIds);

        // 第2回 reload: layer 1 が消えた
        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.DoesNotContain(1, ctrl.VisibleLayerIds);
        Assert.Contains(2, ctrl.VisibleLayerIds);
    }

    [Fact]
    public async Task GetLayerById_ReturnsCanEditTrue()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1, canEdit: true) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        var layer = ctrl.GetLayerById(1);
        Assert.NotNull(layer);
        Assert.True(layer!.CanEdit);
    }

    [Fact]
    public async Task GetLayerById_NonExisting_ReturnsNull()
    {
        var api = new FakeApiClient
        {
            GetLayersImpl = _ => new[] { MakeLayer(1) }
        };
        var ctrl = NewController(api);
        await ctrl.ReloadAsync(null, CancellationToken.None);

        Assert.Null(ctrl.GetLayerById(999));
    }
}
