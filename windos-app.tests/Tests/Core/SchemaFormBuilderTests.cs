using AgriGis.Desktop.Core;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core;

public sealed class SchemaFormBuilderTests
{
    [Fact]
    public void Build_MapsTypeStringsToFieldKind()
    {
        var schema = new LayerSchema(new[]
        {
            new SchemaField("a", "string",  Required: true,  Label: "A"),
            new SchemaField("b", "number",  Required: false, Label: null),
            new SchemaField("c", "integer", Required: false, Label: null),
            new SchemaField("d", "boolean", Required: true,  Label: "D"),
            new SchemaField("e", "date",    Required: false, Label: null),
            new SchemaField("f", "enum",    Required: false, Label: null)  // 未対応 → Unknown
        });

        var fields = SchemaFormBuilder.Build(schema);
        Assert.Equal(6, fields.Count);
        Assert.Equal(FieldKind.String,  fields[0].Kind);
        Assert.Equal(FieldKind.Number,  fields[1].Kind);
        Assert.Equal(FieldKind.Integer, fields[2].Kind);
        Assert.Equal(FieldKind.Boolean, fields[3].Kind);
        Assert.Equal(FieldKind.Date,    fields[4].Kind);
        Assert.Equal(FieldKind.Unknown, fields[5].Kind);
    }

    [Fact]
    public void Build_UsesKeyAsLabelWhenLabelNullOrBlank()
    {
        var schema = new LayerSchema(new[]
        {
            new SchemaField("alpha", "string", Required: false, Label: null),
            new SchemaField("beta",  "string", Required: false, Label: ""),
            new SchemaField("gamma", "string", Required: false, Label: "ガンマ")
        });

        var fields = SchemaFormBuilder.Build(schema);
        Assert.Equal("alpha", fields[0].Label);
        Assert.Equal("beta",  fields[1].Label);
        Assert.Equal("ガンマ", fields[2].Label);
    }

    [Fact]
    public void Build_PreservesRequiredFlag()
    {
        var schema = new LayerSchema(new[]
        {
            new SchemaField("x", "string", Required: true,  Label: null),
            new SchemaField("y", "string", Required: false, Label: null)
        });

        var fields = SchemaFormBuilder.Build(schema);
        Assert.True(fields[0].Required);
        Assert.False(fields[1].Required);
    }
}
