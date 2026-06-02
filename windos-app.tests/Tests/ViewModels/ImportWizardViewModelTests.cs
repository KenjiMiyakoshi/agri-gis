using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// B505 (WB5): ImportWizardViewModel をヘッドレスで検証。Application.Run 不要。
public sealed class ImportWizardViewModelTests
{
    private static string GeoJsonFixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "import", "geojson_point.json");

    [Fact]
    public void Step1_LayerNameEmpty_CanGoNextFalse()
    {
        var vm = new ImportWizardViewModel(new FakeApiClient());
        vm.SourceFormat = "geojson";
        vm.FilePath = GeoJsonFixturePath;
        vm.LayerName = "";
        Assert.False(vm.CanGoNext);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void Step1_AllSet_CanGoNextTrue()
    {
        var vm = new ImportWizardViewModel(new FakeApiClient());
        vm.SourceFormat = "geojson";
        vm.FilePath = GeoJsonFixturePath;
        vm.LayerName = "test layer";
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task LoadSchemaAsync_PopulatesInferredFields()
    {
        var vm = new ImportWizardViewModel(new FakeApiClient());
        vm.SourceFormat = "geojson";
        vm.FilePath = GeoJsonFixturePath;
        vm.LayerName = "test";
        await vm.LoadSchemaAsync(CancellationToken.None);
        Assert.Equal(4, vm.InferredFields.Count); // name, value, active, observed_at
    }

    [Fact]
    public async Task ImportAsync_Succeeds_CallsCreateLayerStartBulkFinalize()
    {
        var api = new FakeApiClient();
        var vm = new ImportWizardViewModel(api);
        vm.SourceFormat = "geojson";
        vm.FilePath = GeoJsonFixturePath;
        vm.LayerName = "import-test";
        await vm.LoadSchemaAsync(CancellationToken.None);
        await vm.ImportAsync(chunkSize: 2, CancellationToken.None);

        Assert.Equal(1, api.CreateLayerCalls);
        Assert.Equal(1, api.StartImportJobCalls);
        Assert.Equal(2, api.BulkInsertCalls);   // 3 features / chunk=2 → 2 chunks
        Assert.Equal(1, api.FinalizeCalls);
        Assert.Equal("succeeded", api.LastFinalizeStatus);
        Assert.Equal(100, vm.CreatedLayerId);
        Assert.Equal(3, vm.Progress);
    }

    [Fact]
    public async Task ImportAsync_BulkFails_FinalizesFailedAndRethrows()
    {
        var api = new FakeApiClient
        {
            BulkInsertImpl = (_, _) => throw new InvalidOperationException("boom")
        };
        var vm = new ImportWizardViewModel(api);
        vm.SourceFormat = "geojson";
        vm.FilePath = GeoJsonFixturePath;
        vm.LayerName = "fail-test";
        await vm.LoadSchemaAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.ImportAsync(chunkSize: 2, CancellationToken.None));

        Assert.Equal(1, api.FinalizeCalls);
        Assert.Equal("failed", api.LastFinalizeStatus);
        Assert.NotNull(vm.LastError);
        Assert.False(vm.IsImporting);
    }
}
