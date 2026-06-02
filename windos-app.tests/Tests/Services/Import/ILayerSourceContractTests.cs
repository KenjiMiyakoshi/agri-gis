using AgriGis.Desktop.Services.Import;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// B503 (WB5): ILayerSource の契約を概観的に検証する抽象テストクラス。
// 各形式 (GeoJsonLayerSource / CsvLayerSource / Phase C GdalLayerSource) は
// このクラスを継承して CreateSource / Expected*** を実装するだけで契約を一斉検証できる。
public abstract class ILayerSourceContractTests
{
    protected abstract ILayerSource CreateSource();

    protected abstract string ExpectedSourceFormat { get; }
    protected abstract int? ExpectedSourceSrid { get; }
    protected abstract int ExpectedFeatureCount { get; }
    protected abstract int ExpectedSchemaFieldCount { get; }

    [Fact]
    public async Task SourceFormat_Matches()
    {
        await using var src = CreateSource();
        Assert.Equal(ExpectedSourceFormat, src.SourceFormat);
    }

    [Fact]
    public async Task SourceSrid_Matches()
    {
        await using var src = CreateSource();
        Assert.Equal(ExpectedSourceSrid, src.SourceSrid);
    }

    [Fact]
    public async Task InferSchema_ReturnsExpectedCount()
    {
        await using var src = CreateSource();
        var fields = await src.InferSchemaAsync(CancellationToken.None);
        Assert.Equal(ExpectedSchemaFieldCount, fields.Count);
    }

    [Fact]
    public async Task ReadFeatures_YieldsExpectedCount()
    {
        await using var src = CreateSource();
        var count = 0;
        await foreach (var _ in src.ReadFeaturesAsync(4326, CancellationToken.None))
        {
            count++;
        }
        Assert.Equal(ExpectedFeatureCount, count);
    }

    [Fact]
    public async Task ReadFeatures_AllGeometriesArePresent()
    {
        await using var src = CreateSource();
        await foreach (var f in src.ReadFeaturesAsync(4326, CancellationToken.None))
        {
            Assert.True(f.Geometry.TryGetProperty("type", out _));
            Assert.True(f.Geometry.TryGetProperty("coordinates", out _));
        }
    }
}
