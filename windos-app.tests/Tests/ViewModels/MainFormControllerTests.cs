using AgriGis.Desktop.Dto;
using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// H5-104 (WH5-1): MainFormController の動作検証。
// UI 非依存ロジック (ComputeRestoreIndex + ReloadAsync の API 呼び出し経路) を検証する。
public sealed class MainFormControllerTests
{
    private static LayerDto MakeLayer(int id, string name = "L") =>
        new(LayerId: id, LayerName: name, LayerType: "polygon",
            OwnerOrgId: null, IsShared: false,
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: 1,
            Schema: new LayerSchemaDto(Array.Empty<SchemaFieldDto>()));

    [Fact]
    public void ComputeRestoreIndex_EmptyLayers_ReturnsMinusOne()
    {
        Assert.Equal(-1, MainFormController.ComputeRestoreIndex(Array.Empty<LayerDto>(), null));
        Assert.Equal(-1, MainFormController.ComputeRestoreIndex(Array.Empty<LayerDto>(), 1));
    }

    [Fact]
    public void ComputeRestoreIndex_NoPrev_ReturnsZero()
    {
        var layers = new[] { MakeLayer(1), MakeLayer(2) };
        Assert.Equal(0, MainFormController.ComputeRestoreIndex(layers, null));
    }

    [Fact]
    public void ComputeRestoreIndex_PrevFound_ReturnsIndex()
    {
        var layers = new[] { MakeLayer(1), MakeLayer(2), MakeLayer(3) };
        Assert.Equal(1, MainFormController.ComputeRestoreIndex(layers, 2));
        Assert.Equal(2, MainFormController.ComputeRestoreIndex(layers, 3));
    }

    [Fact]
    public void ComputeRestoreIndex_PrevNotFound_ReturnsZero()
    {
        var layers = new[] { MakeLayer(1), MakeLayer(2) };
        Assert.Equal(0, MainFormController.ComputeRestoreIndex(layers, 999));
    }

    [Fact]
    public async Task ReloadAsync_PopulatesLayers_AndReturnsCorrectIndex()
    {
        var fakeApi = new FakeApiClient();
        var sessionStore = new InMemorySessionStoreLike();
        var asOf = new AsOfState();
        var controller = new MainFormController(fakeApi, sessionStore, asOf);

        Assert.Empty(controller.Layers);

        var result = await controller.ReloadAsync(prevSelectedLayerId: null, CancellationToken.None);
        // FakeApiClient.GetLayersAsync は Array.Empty<LayerDto>() を返す
        Assert.Empty(result.Layers);
        Assert.Equal(-1, result.RestoreIndex);
    }

    // 簡易 ISessionStore (Phase E' の InMemorySessionStore を test 用に複製)
    private sealed class InMemorySessionStoreLike : AgriGis.Desktop.Auth.ISessionStore
    {
        public AgriGis.Desktop.Auth.Session? Current { get; private set; }
        public event EventHandler? Changed;
        public void Set(AgriGis.Desktop.Auth.Session s) { Current = s; Changed?.Invoke(this, EventArgs.Empty); }
        public void Clear() { Current = null; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
