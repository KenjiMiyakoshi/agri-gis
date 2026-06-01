using AgriGis.Desktop.Core;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core;

public sealed class ProblemDetailsParserTests
{
    [Fact]
    public void Parse_TopLevelErrors_IsExtracted()
    {
        const string json = @"
        {
          ""type"": ""https://httpstatuses.io/422"",
          ""title"": ""Validation failed"",
          ""status"": 422,
          ""requestId"": ""rid-1"",
          ""errors"": [
            { ""attributeKey"": ""name"", ""code"": ""required"", ""message"": ""name is required"" },
            { ""attributeKey"": ""crop"", ""code"": ""type_mismatch"", ""message"": ""crop expects string"" }
          ]
        }";

        var p = ProblemDetailsParser.Parse(json);
        Assert.Equal(422, p.Status);
        Assert.Equal("Validation failed", p.Title);
        Assert.Equal("rid-1", p.RequestId);
        Assert.Equal(2, p.Errors.Count);
        Assert.Equal("name", p.Errors[0].AttributeKey);
        Assert.Equal("type_mismatch", p.Errors[1].Code);
    }

    [Fact]
    public void Parse_ExtensionsErrors_IsExtracted()
    {
        const string json = @"
        {
          ""title"": ""Validation failed"",
          ""status"": 422,
          ""extensions"": {
            ""requestId"": ""rid-2"",
            ""errors"": [
              { ""attributeKey"": ""name"", ""code"": ""required"", ""message"": ""..."" }
            ]
          }
        }";

        var p = ProblemDetailsParser.Parse(json);
        Assert.Equal(422, p.Status);
        Assert.Equal("rid-2", p.RequestId);
        var e = Assert.Single(p.Errors);
        Assert.Equal("name", e.AttributeKey);
    }

    [Fact]
    public void Parse_StatusAndTitle_OnlyButNoErrors()
    {
        const string json = @"{ ""status"": 404, ""title"": ""Not found"" }";
        var p = ProblemDetailsParser.Parse(json);
        Assert.Equal(404, p.Status);
        Assert.Equal("Not found", p.Title);
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmptyProblem()
    {
        var p = ProblemDetailsParser.Parse("{not-json}");
        Assert.Null(p.Status);
        Assert.Null(p.Title);
        Assert.Empty(p.Errors);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyProblem()
    {
        var p = ProblemDetailsParser.Parse("");
        Assert.Null(p.Status);
        Assert.Empty(p.Errors);
    }
}
