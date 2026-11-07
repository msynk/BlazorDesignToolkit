using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

/// <summary>Fixtures adapted from the examples of the DTCG 2025.10 format and color modules.</summary>
public class ConformanceTests
{
    [Theory]
    [InlineData("color-module-examples.tokens.json")]
    [InlineData("group-extends.tokens.json")]
    [InlineData("root-tokens.tokens.json")]
    [InlineData("json-pointer.tokens.json")]
    [InlineData("component-references.tokens.json")]
    [InlineData("composite-aliases.tokens.json")]
    [InlineData("all-types.tokens.json")]
    public void Specification_examples_resolve_without_errors(string fixture)
    {
        var set = TestHelpers.ResolveFixture("conformance", fixture);
        set.AssertNoErrors();
        Assert.All(set.Tokens, t => Assert.True(t.IsValid, $"{t.Path} is invalid"));
    }

    [Fact]
    public void Color_module_examples()
    {
        var set = TestHelpers.ResolveFixture("conformance", "color-module-examples.tokens.json");
        var translucent = (ColorValue)set["Translucent shadow"]!.Value!;
        Assert.Equal(0.5, translucent.Alpha);
        var none = (ColorValue)set["White none hue"]!.Value!;
        Assert.Null(none.Components[0]);
        Assert.Equal("#ffffff", none.ToSrgb().ToHex());
        Assert.Equal("#ff00ff", ((ColorValue)set["Hot pink lab"]!.Value!).ToSrgb().ToHex());
        // Erratum in DTCG 2025.10 example 6: hsl(330 100% 50%) is #ff0080, not the declared hex #ff00ff (magenta has hue 300).
        // The toolkit keeps the components authoritative and reports the mismatch instead of silently picking one.
        Assert.Equal("#ff0080", ((ColorValue)set["Hot pink hsl"]!.Value!).ToSrgb().ToHex());
        var mismatch = set.Diagnostics.Single(DiagnosticCatalog.HexMismatch.Id);
        Assert.Equal("Hot pink hsl", mismatch.TokenPath);
        Assert.Equal(DiagnosticSeverity.Warning, mismatch.Severity);
    }

    [Fact]
    public void Group_extension_inherits_and_overrides()
    {
        var set = TestHelpers.ResolveFixture("conformance", "group-extends.tokens.json");
        Assert.Equal("#cc0066", ((ColorValue)set["button-primary.background"]!.Value!).Hex);
        var inherited = set["button-primary.text"]!;
        Assert.Equal("#ffffff", ((ColorValue)inherited.Value!).Hex);
        Assert.Equal(TokenTypes.Color, inherited.Type);
        Assert.Equal(TokenPath.Parse("button.text"), inherited.InheritedFrom);
    }

    [Fact]
    public void Root_tokens_are_addressed_with_root_segment()
    {
        var set = TestHelpers.ResolveFixture("conformance", "root-tokens.tokens.json");
        Assert.Equal(TokenPath.Parse("color.accent.$root"), set["color.brand"]!.AliasOf);
        Assert.Equal("#dd0000", ((ColorValue)set["color.brand"]!.Value!).Hex);
    }

    [Fact]
    public void Referencing_a_group_suggests_its_root_token()
    {
        var json = TestHelpers.ReadFixture("conformance", "root-tokens.tokens.json").Replace("{color.accent.$root}", "{color.accent}");
        var d = TestHelpers.Resolve(json).Diagnostics.Single(DiagnosticCatalog.ReferenceTargetsGroup.Id);
        Assert.Contains("{color.accent.$root}", d.Remediation);
    }

    [Fact]
    public void Json_pointer_references_resolve_whole_values_and_sub_values()
    {
        var set = TestHelpers.ResolveFixture("conformance", "json-pointer.tokens.json");
        Assert.Equal("#0066cc", ((ColorValue)set["semantic.primary"]!.Value!).Hex);
        Assert.Equal(TokenPath.Parse("colors.blue"), set["semantic.primary"]!.AliasOf);
        Assert.Equal(0, ((NumberValue)set["semantic.primaryHue"]!.Value!).Value);
        Assert.Contains(TokenPath.Parse("colors.blue"), set["semantic.primaryHue"]!.References);
    }

    [Fact]
    public void Token_level_pointer_round_trips()
    {
        var json = TestHelpers.ReadFixture("conformance", "json-pointer.tokens.json");
        var output = DtcgWriter.Write(DtcgReader.Read(json));
        Assert.True(SourceNode.DeepEquals(SourceJsonParser.Parse(json).Root, SourceJsonParser.Parse(output).Root));
    }

    [Fact]
    public void Pointer_references_inside_color_components()
    {
        var set = TestHelpers.ResolveFixture("conformance", "component-references.tokens.json");
        var color = (ColorValue)set["semantic.primary"]!.Value!;
        Assert.Equal([0.2, 0.4, 0.7], color.Components.Select(c => c!.Value));
    }

    [Fact]
    public void Composite_aliases_resolve_sub_values_and_whole_tokens()
    {
        var set = TestHelpers.ResolveFixture("conformance", "composite-aliases.tokens.json");
        var shadow = (ShadowValue)set["component.card.box-shadow"]!.Value!;
        var layer = Assert.Single(shadow.Layers);
        Assert.Equal(0.5, layer.OffsetX.Value);
        Assert.Equal("rem", layer.OffsetX.Unit);
        Assert.Equal(0.5, layer.Color.Alpha);
        Assert.Equal(TokenTypes.Shadow, set["component.card.box-shadow"]!.Type);
    }

    [Fact]
    public void All_types_are_parsed_into_typed_values()
    {
        var set = TestHelpers.ResolveFixture("conformance", "all-types.tokens.json");
        Assert.Equal(950, ((FontWeightValue)set["font.weight-keyword"]!.Value!).Weight);
        Assert.Equal(["Helvetica", "Arial", "sans-serif"], ((FontFamilyValue)set["font.stack"]!.Value!).Families);
        Assert.Equal(1500, ((DurationValue)set["duration.long"]!.Value!).Milliseconds);
        var stroke = (StrokeStyleValue)set["stroke.object"]!.Value!;
        Assert.Equal("round", stroke.LineCap);
        Assert.Equal(8, stroke.DashArray![1].Value);
        var border = (BorderValue)set["border.heavy"]!.Value!;
        Assert.Equal("dashed", border.Style.Keyword);
        var transition = (TransitionValue)set["transition.emphasis"]!.Value!;
        Assert.Equal(0.5, transition.TimingFunction.X1);
        var shadow = (ShadowValue)set["shadow.layered"]!.Value!;
        Assert.True(shadow.IsArrayForm);
        Assert.True(shadow.Layers[1].Inset);
        var gradient = (GradientValue)set["gradient.sunset"]!.Value!;
        Assert.Equal(0.5, gradient.Stops[1].Position);
        var typography = (TypographyValue)set["typography.heading"]!.Value!;
        Assert.Equal(1.2, typography.LineHeight);
        Assert.Equal("extra-black", typography.FontWeight.Keyword);
    }

    [Fact]
    public void Sample_design_system_resolves_cleanly_in_every_mode()
    {
        foreach (var semantic in new[] { "semantic.light.tokens.json", "semantic.dark.tokens.json" })
        {
            foreach (var density in new[] { "density.comfortable.tokens.json", "density.compact.tokens.json" })
            {
                var set = TestHelpers.ResolveSamples("foundation.tokens.json", semantic, "density.comfortable.tokens.json", density, "components.tokens.json");
                set.AssertNoErrors();
                Assert.All(set.Tokens, t => Assert.True(t.IsValid, t.Path.ToString()));
            }
        }
    }

    [Fact]
    public void Color_space_conversions_match_specification_vectors()
    {
        // #ff00ff expressed in each color space (values from the DTCG color module examples, rounded there).
        var expected = new Dictionary<string, double[]>
        {
            [ColorSpaces.Hsl] = [300, 100, 50],
            [ColorSpaces.Hwb] = [300, 0, 0],
            [ColorSpaces.Lab] = [60.17, 93.54, -60.5],
            [ColorSpaces.Lch] = [60.17, 111.4, 327.11],
            [ColorSpaces.Oklab] = [0.701, 0.2746, -0.169],
            [ColorSpaces.Oklch] = [0.7016, 0.3225, 328.363],
            [ColorSpaces.XyzD65] = [0.5929, 0.2848, 0.9699],
            [ColorSpaces.XyzD50] = [0.5791, 0.2831, 0.728],
        };
        foreach (var (space, components) in expected)
        {
            var converted = ColorMath.Convert(ColorSpaces.Srgb, [1, 0, 1], space);
            for (var i = 0; i < 3; i++)
            {
                var tolerance = Math.Max(Math.Abs(components[i]) * 0.003, 0.002);
                Assert.True(Math.Abs(converted[i] - components[i]) <= tolerance,
                    $"{space}[{i}]: expected {components[i]}, got {converted[i]}");
            }
            var back = ColorMath.ToSrgb(space, components.Select(c => (double?)c).ToArray());
            Assert.Equal("#ff00ff", back.ToHex());
        }
    }
}
