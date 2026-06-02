using AgriGis.Desktop.Services.Import.InferenceStrategies;
using OSGeo.OGR;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Gdal;

// WC4 C502: GdalInferenceStrategy 純粋関数の [Theory]
// OGR を実体起動するため [Collection("Gdal")] で fixture 共有。
[Collection(GdalCollection.Name)]
public sealed class GdalInferenceStrategyTests
{
    private static (DataSource ds, Layer layer) CreateMemoryLayer()
    {
        var driver = Ogr.GetDriverByName("MEMORY");
        Assert.NotNull(driver);
        var ds = driver.CreateDataSource("test", null);
        var layer = ds.CreateLayer("test_layer", null, wkbGeometryType.wkbPoint, null);
        return (ds, layer);
    }

    private static void AddFeature(Layer layer, FieldDefn[] fields, object?[] values)
    {
        using var feat = new Feature(layer.GetLayerDefn());
        for (int i = 0; i < fields.Length; i++)
        {
            if (values[i] is null) { feat.UnsetField(i); continue; }
            switch (fields[i].GetFieldType())
            {
                case FieldType.OFTInteger:
                    feat.SetField(i, Convert.ToInt32(values[i]));
                    break;
                case FieldType.OFTReal:
                    feat.SetField(i, Convert.ToDouble(values[i]));
                    break;
                default:
                    feat.SetField(i, (string)values[i]!);
                    break;
            }
        }
        layer.CreateFeature(feat);
    }

    [Theory]
    [InlineData(FieldType.OFTInteger, "integer")]
    [InlineData(FieldType.OFTInteger64, "integer")]
    [InlineData(FieldType.OFTReal, "number")]
    [InlineData(FieldType.OFTString, "string")]
    [InlineData(FieldType.OFTDate, "date")]
    [InlineData(FieldType.OFTDateTime, "date")]
    [InlineData(FieldType.OFTStringList, "string")]
    [InlineData(FieldType.OFTIntegerList, "string")]
    public void Infer_SingleField_MapsOftToType(FieldType oft, string expected)
    {
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("x", oft))
        {
            layer.CreateField(fd, 1);
            var fields = GdalInferenceStrategy.Infer(layer);
            Assert.Contains(fields, f => f.Name == "x" && f.Type == expected);
        }
    }

    [Fact]
    public void Infer_OftBinary_IsSkippedWithWarning()
    {
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("blob", FieldType.OFTBinary))
        {
            layer.CreateField(fd, 1);
            var warnings = new List<string>();
            var fields = GdalInferenceStrategy.Infer(layer, warnings);
            Assert.DoesNotContain(fields, f => f.Name == "blob");
            Assert.Contains(warnings, w => w.Contains("OFTBinary", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Infer_NullValues_MarkNullable()
    {
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("v", FieldType.OFTInteger))
        {
            layer.CreateField(fd, 1);
            AddFeature(layer, new[] { fd }, new object?[] { 1 });
            AddFeature(layer, new[] { fd }, new object?[] { null });
            AddFeature(layer, new[] { fd }, new object?[] { 3 });

            var fields = GdalInferenceStrategy.Infer(layer);
            var x = fields.Single(f => f.Name == "v");
            Assert.True(x.Nullable);
            Assert.False(x.Required);
        }
    }

    [Fact]
    public void Infer_AllIso8601String_PromotedToDate()
    {
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("observed", FieldType.OFTString))
        {
            fd.SetWidth(10);
            layer.CreateField(fd, 1);
            AddFeature(layer, new[] { fd }, new object?[] { "2026-06-01" });
            AddFeature(layer, new[] { fd }, new object?[] { "2026-06-02" });
            AddFeature(layer, new[] { fd }, new object?[] { "2026-06-03" });

            var fields = GdalInferenceStrategy.Infer(layer);
            var x = fields.Single(f => f.Name == "observed");
            Assert.Equal("date", x.Type);
        }
    }
}
