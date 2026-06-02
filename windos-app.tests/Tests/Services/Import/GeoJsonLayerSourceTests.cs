using AgriGis.Desktop.Services.Import;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

public sealed class GeoJsonLayerSourceTests : ILayerSourceContractTests
{
    private static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "import", "geojson_point.json");

    protected override ILayerSource CreateSource() => new GeoJsonLayerSource(FixturePath);
    protected override string ExpectedSourceFormat => "geojson";
    protected override int? ExpectedSourceSrid => 4326;
    protected override int ExpectedFeatureCount => 3;
    // properties: name, value, active, observed_at
    protected override int ExpectedSchemaFieldCount => 4;
}
