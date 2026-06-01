using System.Text.Json;
using AgriGis.Desktop.Core;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core;

public sealed class AttributeValidatorTests
{
    private static LayerSchema NameRequired => new(new[]
    {
        new SchemaField("name", "string", Required: true, Label: "Name"),
        new SchemaField("crop", "string", Required: false, Label: null)
    });

    private static Dictionary<string, JsonElement> Dict(params (string k, string json)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (var (k, j) in pairs)
        {
            d[k] = JsonDocument.Parse(j).RootElement.Clone();
        }
        return d;
    }

    [Fact]
    public void Validate_RequiredMissing_ReturnsRequiredError()
    {
        var errors = AttributeValidator.Validate(NameRequired, Dict());
        var e = Assert.Single(errors);
        Assert.Equal("name", e.AttributeKey);
        Assert.Equal("required", e.Code);
    }

    [Fact]
    public void Validate_RequiredNull_ReturnsRequiredError()
    {
        var errors = AttributeValidator.Validate(NameRequired, Dict(("name", "null")));
        var e = Assert.Single(errors);
        Assert.Equal("name", e.AttributeKey);
        Assert.Equal("required", e.Code);
    }

    [Fact]
    public void Validate_TypeMismatch_ReturnsTypeMismatchError()
    {
        var errors = AttributeValidator.Validate(NameRequired, Dict(("name", "123")));
        var e = Assert.Single(errors);
        Assert.Equal("name", e.AttributeKey);
        Assert.Equal("type_mismatch", e.Code);
    }

    [Fact]
    public void Validate_Legal_ReturnsEmpty()
    {
        var errors = AttributeValidator.Validate(NameRequired, Dict(("name", "\"A\""), ("crop", "\"wheat\"")));
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_OptionalMissing_IsAccepted()
    {
        var errors = AttributeValidator.Validate(NameRequired, Dict(("name", "\"A\"")));
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_IntegerType_AcceptsInt_RejectsFloat()
    {
        var schema = new LayerSchema(new[]
        {
            new SchemaField("n", "integer", Required: true, Label: null)
        });

        Assert.Empty(AttributeValidator.Validate(schema, Dict(("n", "42"))));

        var errs = AttributeValidator.Validate(schema, Dict(("n", "\"x\"")));
        var e = Assert.Single(errs);
        Assert.Equal("type_mismatch", e.Code);
    }

    [Fact]
    public void Validate_BooleanType_AcceptsTrueFalse_RejectsString()
    {
        var schema = new LayerSchema(new[]
        {
            new SchemaField("b", "boolean", Required: true, Label: null)
        });

        Assert.Empty(AttributeValidator.Validate(schema, Dict(("b", "true"))));
        Assert.Empty(AttributeValidator.Validate(schema, Dict(("b", "false"))));

        var errs = AttributeValidator.Validate(schema, Dict(("b", "\"true\"")));
        var e = Assert.Single(errs);
        Assert.Equal("type_mismatch", e.Code);
    }
}
