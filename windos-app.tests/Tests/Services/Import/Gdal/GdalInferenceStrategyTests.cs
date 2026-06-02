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
    public void Infer_OftIntegerWithBooleanSubType_MapsToBoolean()
    {
        // Phase C 実機 smoke test 回帰防止: OGR の OFSTBoolean subtype は
        // "integer" ではなく "boolean" に推論する必要がある (API バリデータ整合)。
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("active", FieldType.OFTInteger))
        {
            fd.SetSubType(FieldSubType.OFSTBoolean);
            layer.CreateField(fd, 1);
            var fields = GdalInferenceStrategy.Infer(layer);
            var x = fields.Single(f => f.Name == "active");
            Assert.Equal("boolean", x.Type);
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
    public void Infer_TotalExceedsSample_Nullable_ConservativeRequired()
    {
        // Phase C smoke test 6件目バグ回帰防止: layer の実件数が SampleSize (100) を
        // 超える場合、先頭 100 件が全部 non-empty でも sample 外に空値が潜む可能性が
        // あるため Required=false / Nullable=true を保守的に採用する。
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("code", FieldType.OFTString))
        {
            fd.SetWidth(8);
            layer.CreateField(fd, 1);

            // 先頭 100 件は全部 non-empty。101 件目以降に空が混ざる SHP を模擬する
            // ためにここでは 150 件投入 (sample 100 < 150 = totalFeatures)。
            for (int i = 0; i < 150; i++)
            {
                AddFeature(layer, new[] { fd }, new object?[] { $"v{i:D3}" });
            }

            var fields = GdalInferenceStrategy.Infer(layer);
            var code = fields.Single(f => f.Name == "code");
            Assert.True(code.Nullable, "sample が全件カバーしないなら保守的に Nullable=true");
            Assert.False(code.Required, "sample が全件カバーしないなら保守的に Required=false");
        }
    }

    [Fact]
    public void Infer_TotalEqualsSample_StrictRequired_WhenAllNonEmpty()
    {
        // 実件数が SampleSize 以下なら sample == 全件なので Strict mode で判定する。
        // 全件 non-empty なら Required=true を維持。
        var (ds, layer) = CreateMemoryLayer();
        using (ds)
        using (var fd = new FieldDefn("code", FieldType.OFTString))
        {
            fd.SetWidth(8);
            layer.CreateField(fd, 1);
            for (int i = 0; i < 10; i++)
            {
                AddFeature(layer, new[] { fd }, new object?[] { $"v{i}" });
            }

            var fields = GdalInferenceStrategy.Infer(layer);
            var code = fields.Single(f => f.Name == "code");
            Assert.False(code.Nullable);
            Assert.True(code.Required);
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
