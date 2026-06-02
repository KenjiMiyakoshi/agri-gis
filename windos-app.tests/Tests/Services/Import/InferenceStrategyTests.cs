using System.Text.Json;
using AgriGis.Desktop.Services.Import.InferenceStrategies;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// B504 (WB5): スキーマ推論の純粋関数 [Theory] テスト。DB/API 依存ゼロ。
public sealed class GeoJsonInferenceStrategyTests
{
    [Theory]
    [InlineData("12", "integer")]
    [InlineData("3.14", "number")]
    [InlineData("true", "boolean")]
    [InlineData("\"hello\"", "string")]
    [InlineData("\"2026-06-01\"", "date")]
    public void Infer_SingleValueKind_MapsToType(string jsonValueLiteral, string expectedType)
    {
        var fc = $"{{\"features\":[{{\"properties\":{{\"x\":{jsonValueLiteral}}}}}]}}";
        using var doc = JsonDocument.Parse(fc);
        var fields = GeoJsonInferenceStrategy.Infer(doc.RootElement.GetProperty("features"));
        var x = fields.Single(f => f.Name == "x");
        Assert.Equal(expectedType, x.Type);
    }

    [Fact]
    public void Infer_MixedIntegerAndNumber_PromotesToNumber()
    {
        var fc = "{\"features\":[{\"properties\":{\"x\":1}},{\"properties\":{\"x\":2.5}}]}";
        using var doc = JsonDocument.Parse(fc);
        var fields = GeoJsonInferenceStrategy.Infer(doc.RootElement.GetProperty("features"));
        var x = fields.Single(f => f.Name == "x");
        Assert.Equal("number", x.Type);
    }

    [Fact]
    public void Infer_NullObserved_MarksNullable()
    {
        var fc = "{\"features\":[{\"properties\":{\"x\":1}},{\"properties\":{\"x\":null}}]}";
        using var doc = JsonDocument.Parse(fc);
        var fields = GeoJsonInferenceStrategy.Infer(doc.RootElement.GetProperty("features"));
        var x = fields.Single(f => f.Name == "x");
        Assert.True(x.Nullable);
        Assert.False(x.Required);
    }

    [Fact]
    public void Infer_MixedTypes_FallsBackToString()
    {
        var fc = "{\"features\":[{\"properties\":{\"x\":1}},{\"properties\":{\"x\":\"abc\"}}]}";
        using var doc = JsonDocument.Parse(fc);
        var fields = GeoJsonInferenceStrategy.Infer(doc.RootElement.GetProperty("features"));
        var x = fields.Single(f => f.Name == "x");
        Assert.Equal("string", x.Type);
    }
}

public sealed class CsvInferenceStrategyTests
{
    [Theory]
    [InlineData("12", "integer")]
    [InlineData("3.14", "number")]
    [InlineData("true", "boolean")]
    [InlineData("2026-06-01", "date")]
    [InlineData("hello", "string")]
    public void Infer_SingleColumn_MapsToType(string value, string expectedType)
    {
        var fields = CsvInferenceStrategy.Infer(
            headers: new[] { "lon", "lat", "x" },
            sampleRows: new[] { new[] { "0", "0", value } },
            lonColIndex: 0, latColIndex: 1);
        var x = fields.Single(f => f.Name == "x");
        Assert.Equal(expectedType, x.Type);
    }

    [Fact]
    public void Infer_EmptyCells_MarkNullable()
    {
        var fields = CsvInferenceStrategy.Infer(
            headers: new[] { "lon", "lat", "x" },
            sampleRows: new[]
            {
                new[] { "0", "0", "12" },
                new[] { "1", "1", "" }
            },
            lonColIndex: 0, latColIndex: 1);
        var x = fields.Single(f => f.Name == "x");
        Assert.True(x.Nullable);
    }

    [Fact]
    public void Infer_LonLatColumns_AreExcluded()
    {
        var fields = CsvInferenceStrategy.Infer(
            headers: new[] { "lon", "lat", "name" },
            sampleRows: new[] { new[] { "0", "0", "p1" } },
            lonColIndex: 0, latColIndex: 1);
        Assert.DoesNotContain(fields, f => f.Name == "lon" || f.Name == "lat");
        Assert.Single(fields, f => f.Name == "name");
    }
}
