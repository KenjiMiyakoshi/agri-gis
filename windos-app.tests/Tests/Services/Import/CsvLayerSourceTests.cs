using AgriGis.Desktop.Services.Import;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

public sealed class CsvLayerSourceTests : ILayerSourceContractTests
{
    private static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "import", "csv_latlng.csv");

    // CSV ヘッダ: lon, lat, name, value, active  → lon=0, lat=1
    protected override ILayerSource CreateSource()
        => new CsvLayerSource(FixturePath, lonColIndex: 0, latColIndex: 1, sourceSrid: 4326);

    protected override string ExpectedSourceFormat => "csv";
    protected override int? ExpectedSourceSrid => 4326;
    protected override int ExpectedFeatureCount => 3;
    // lon, lat 列を除いた properties: name, value, active
    protected override int ExpectedSchemaFieldCount => 3;
}
