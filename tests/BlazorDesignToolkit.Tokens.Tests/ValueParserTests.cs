using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

public class ValueParserTests
{
    private static ResolvedTokenSet Parse(string type, string value, TokenResolutionOptions? options = null)
        => TestHelpers.Resolve(TestHelpers.Token(type, value), options);

    [Theory]
    [InlineData("color", "\"#ff0000\"")]
    [InlineData("color", """{ "colorSpace": "rgb", "components": [1, 0, 0] }""")]
    [InlineData("color", """{ "colorSpace": "srgb", "components": [1, 0] }""")]
    [InlineData("color", """{ "colorSpace": "srgb", "components": [1, 0, "zero"] }""")]
    [InlineData("color", """{ "colorSpace": "srgb", "components": [1, 0, 0], "alpha": 2 }""")]
    [InlineData("color", """{ "colorSpace": "srgb", "components": [1, 0, 0], "hex": "#ff000080" }""")]
    [InlineData("color", """{ "components": [1, 0, 0] }""")]
    [InlineData("dimension", "\"16px\"")]
    [InlineData("dimension", """{ "value": 1, "unit": "em" }""")]
    [InlineData("dimension", """{ "value": 0 }""")]
    [InlineData("dimension", """{ "value": "1", "unit": "px" }""")]
    [InlineData("duration", """{ "value": 1, "unit": "min" }""")]
    [InlineData("fontFamily", "[]")]
    [InlineData("fontFamily", "[\"a\", 1]")]
    [InlineData("fontWeight", "1001")]
    [InlineData("fontWeight", "0")]
    [InlineData("fontWeight", "\"Bold\"")]
    [InlineData("fontWeight", "\"semibold\"")]
    [InlineData("cubicBezier", "[0, 0, 1]")]
    [InlineData("cubicBezier", "[1.5, 0, 1, 1]")]
    [InlineData("number", "\"1\"")]
    [InlineData("strokeStyle", "\"wavy\"")]
    [InlineData("strokeStyle", """{ "dashArray": [], "lineCap": "round" }""")]
    [InlineData("strokeStyle", """{ "dashArray": [{ "value": 1, "unit": "px" }], "lineCap": "flat" }""")]
    [InlineData("border", """{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "width": { "value": 1, "unit": "px" } }""")]
    [InlineData("border", """{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "width": { "value": 1, "unit": "px" }, "style": "solid", "radius": 2 }""")]
    [InlineData("transition", """{ "duration": { "value": 1, "unit": "s" } }""")]
    [InlineData("shadow", "[]")]
    [InlineData("shadow", """{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "offsetX": { "value": 0, "unit": "px" }, "offsetY": { "value": 0, "unit": "px" }, "blur": { "value": 0, "unit": "px" }, "spread": { "value": 0, "unit": "px" }, "inset": "yes" }""")]
    [InlineData("gradient", "[]")]
    [InlineData("gradient", """[{ "color": { "colorSpace": "srgb", "components": [0,0,0] } }]""")]
    [InlineData("typography", """{ "fontFamily": "A", "fontSize": { "value": 1, "unit": "rem" }, "fontWeight": 400, "letterSpacing": { "value": 0, "unit": "px" } }""")]
    public void Invalid_values_are_rejected(string type, string value)
    {
        var set = Parse(type, value);
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidValue.Id && d.TokenPath == "t");
        Assert.False(set["t"]!.IsValid);
    }

    [Fact]
    public void Invalid_value_diagnostics_point_into_the_value()
    {
        var set = Parse("border", """{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "width": { "value": 1, "unit": "em" }, "style": "solid" }""");
        var d = set.Diagnostics.Single(DiagnosticCatalog.InvalidValue.Id);
        Assert.Contains("$value.width.unit", d.Message);
        Assert.Equal("px | rem", d.Expected);
        Assert.Equal("em", d.Actual);
    }

    [Fact]
    public void Additional_units_are_accepted_with_a_warning()
    {
        var options = new TokenResolutionOptions { Values = new ValueParseOptions { AdditionalDimensionUnits = new HashSet<string> { "em" } } };
        var set = Parse("dimension", """{ "value": 1.5, "unit": "em" }""", options);
        Assert.True(set["t"]!.IsValid);
        Assert.Equal(DiagnosticSeverity.Warning, set.Diagnostics.Single(DiagnosticCatalog.NonStandardUnit.Id).Severity);
    }

    [Theory]
    [InlineData("color", "\"#ff8800\"")]
    [InlineData("dimension", "\"16px\"")]
    [InlineData("duration", "\"0.2s\"")]
    public void Legacy_formats_are_accepted_only_when_enabled(string type, string value)
    {
        Assert.False(Parse(type, value)["t"]!.IsValid);
        var set = Parse(type, value, new TokenResolutionOptions { Values = new ValueParseOptions { AllowLegacyValueFormats = true } });
        Assert.True(set["t"]!.IsValid);
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.LegacyValueFormat.Id);
    }

    [Fact]
    public void Out_of_range_components_warn_but_parse()
    {
        var set = Parse("color", """{ "colorSpace": "hsl", "components": [360, 120, 50] }""");
        Assert.True(set["t"]!.IsValid);
        Assert.Equal(2, set.Diagnostics.Count(d => d.RuleId == DiagnosticCatalog.ValueOutOfRange.Id));
    }

    [Fact]
    public void Unknown_color_properties_warn()
    {
        var set = Parse("color", """{ "colorSpace": "srgb", "components": [0, 0, 0], "name": "black" }""");
        Assert.True(set["t"]!.IsValid);
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnknownValueProperty.Id);
    }

    [Fact]
    public void Gradient_positions_are_clamped_with_a_warning()
    {
        var set = Parse("gradient", """[{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "position": -0.5 }, { "color": { "colorSpace": "srgb", "components": [1,1,1] }, "position": 1.5 }]""");
        var g = (GradientValue)set["t"]!.Value!;
        Assert.Equal([0d, 1d], g.Stops.Select(s => s.Position));
        Assert.Equal(2, set.Diagnostics.Count(d => d.RuleId == DiagnosticCatalog.ValueOutOfRange.Id));
    }

    [Fact]
    public void Nested_shadow_arrays_from_references_are_rejected()
    {
        var set = TestHelpers.Resolve("""
        {
          "c": { "$type": "color", "$value": { "colorSpace": "srgb", "components": [0, 0, 0] } },
          "multi": { "$type": "shadow", "$value": [
            { "color": "{c}", "offsetX": { "value": 0, "unit": "px" }, "offsetY": { "value": 1, "unit": "px" }, "blur": { "value": 1, "unit": "px" }, "spread": { "value": 0, "unit": "px" } },
            { "color": "{c}", "offsetX": { "value": 0, "unit": "px" }, "offsetY": { "value": 2, "unit": "px" }, "blur": { "value": 1, "unit": "px" }, "spread": { "value": 0, "unit": "px" } } ] },
          "nested": { "$type": "shadow", "$value": ["{multi}"] }
        }
        """);
        Assert.True(set["multi"]!.IsValid);
        Assert.False(set["nested"]!.IsValid);
        Assert.Contains(set.Diagnostics, d => d.TokenPath == "nested" && d.Message.Contains("array flattening"));
    }

    [Fact]
    public void Gradient_references_insert_stops()
    {
        var set = TestHelpers.Resolve("""
        {
          "base": { "$type": "gradient", "$value": [{ "color": { "colorSpace": "srgb", "components": [0,0,0] }, "position": 0 }, { "color": { "colorSpace": "srgb", "components": [1,1,1] }, "position": 0.5 }] },
          "extended": { "$type": "gradient", "$value": ["{base}", { "color": { "colorSpace": "srgb", "components": [1,0,0] }, "position": 1 }] }
        }
        """);
        set.AssertNoErrors();
        Assert.Equal(3, ((GradientValue)set["extended"]!.Value!).Stops.Count);
    }

    [Fact]
    public void Hex_fallback_mismatch_is_reported()
    {
        var set = Parse("color", """{ "colorSpace": "srgb", "components": [1, 0, 0], "hex": "#00ff00" }""");
        var d = set.Diagnostics.Single(DiagnosticCatalog.HexMismatch.Id);
        Assert.Equal("#ff0000", d.Expected);
    }

    [Fact]
    public void Font_weight_keyword_suggestions()
    {
        var d = Parse("fontWeight", "\"SemiBold\"").Diagnostics.Single(DiagnosticCatalog.InvalidValue.Id);
        Assert.Contains("semi-bold", d.Remediation);
    }
}
