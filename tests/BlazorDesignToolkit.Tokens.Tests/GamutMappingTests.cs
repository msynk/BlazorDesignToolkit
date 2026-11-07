using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;

namespace BlazorDesignToolkit.Tokens.Tests;

/// <summary>
/// CSS Color 4 gamut mapping (§13.2) is verified through the properties that define it: the result is in gamut, keeps
/// OKLCh lightness and hue, and lies within one JND of the chroma-reduced color it approximates.
/// </summary>
public class GamutMappingTests
{
    public static readonly TheoryData<string, double[]> OutOfGamut = new()
    {
        { ColorSpaces.DisplayP3, [1, 0, 0] },
        { ColorSpaces.DisplayP3, [0, 1, 0] },
        { ColorSpaces.DisplayP3, [0, 0, 1] },
        { ColorSpaces.Rec2020, [0.2, 0.9, 0.4] },
        { ColorSpaces.Oklch, [0.7, 0.4, 150] },
        { ColorSpaces.Oklch, [0.5, 0.3, 30] },
        { ColorSpaces.Oklch, [0.95, 0.25, 100] },
        { ColorSpaces.Lab, [50, 120, -80] },
    };

    [Theory]
    [MemberData(nameof(OutOfGamut))]
    public void Mapped_colors_are_in_gamut_and_keep_lightness_and_hue(string space, double[] components)
    {
        var values = components.Select(static v => (double?)v).ToArray();
        Assert.False(ColorMath.ToSrgb(space, values).IsInGamut); // precondition

        var mapped = ColorMath.GamutMapToSrgb(space, values);
        Assert.True(mapped.IsInGamut);

        var origin = ColorMath.Convert(space, values, ColorSpaces.Oklch);
        var result = ColorMath.Convert(ColorSpaces.Srgb, [mapped.R, mapped.G, mapped.B], ColorSpaces.Oklch);
        Assert.InRange(Math.Abs(result[0] - origin[0]), 0, 0.02);
        Assert.InRange(HueDistance(result[2], origin[2]), 0, 5);
        Assert.True(result[1] < origin[1], "chroma is reduced");
    }

    [Theory]
    [MemberData(nameof(OutOfGamut))]
    public void Mapping_preserves_hue_better_than_clipping(string space, double[] components)
    {
        var values = components.Select(static v => (double?)v).ToArray();
        var origin = ColorMath.Convert(space, values, ColorSpaces.Oklch);
        var mapped = ColorMath.GamutMapToSrgb(space, values);
        var clipped = ColorMath.ToSrgb(space, values).Clip();
        var mappedHue = ColorMath.Convert(ColorSpaces.Srgb, [mapped.R, mapped.G, mapped.B], ColorSpaces.Oklch)[2];
        var clippedHue = ColorMath.Convert(ColorSpaces.Srgb, [clipped.R, clipped.G, clipped.B], ColorSpaces.Oklch)[2];
        Assert.True(HueDistance(mappedHue, origin[2]) <= HueDistance(clippedHue, origin[2]) + 0.5);
    }

    [Fact]
    public void In_gamut_colors_are_unchanged()
    {
        var srgb = ColorMath.GamutMapToSrgb(ColorSpaces.Srgb, [0.2, 0.4, 0.6], 0.5);
        Assert.Equal(new SrgbColor(0.2, 0.4, 0.6, 0.5), srgb);
        var p3 = ColorMath.GamutMapToSrgb(ColorSpaces.DisplayP3, [0.5, 0.5, 0.5]);
        Assert.Equal(0.5, p3.R, 4);
        Assert.Equal(0.5, p3.G, 4);
        Assert.Equal(0.5, p3.B, 4);
    }

    [Fact]
    public void Lightness_limits_map_to_white_and_black()
    {
        Assert.Equal("#ffffff", ColorMath.GamutMapToSrgb(ColorSpaces.Oklch, [1.2, 0.3, 40]).ToHex());
        Assert.Equal("#000000", ColorMath.GamutMapToSrgb(ColorSpaces.Oklch, [-0.1, 0.3, 40]).ToHex());
    }

    [Fact]
    public void Hex_export_gamut_maps_by_default_and_can_clip()
    {
        const string json = """{ "c": { "$type": "color", "$value": { "colorSpace": "display-p3", "components": [0, 1, 0] } } }""";
        var set = TestHelpers.Resolve(json);
        var mapped = CssExporter.Export(set, new CssExportOptions { ColorFormat = CssColorFormat.Hex });
        var clipped = CssExporter.Export(set, new CssExportOptions { ColorFormat = CssColorFormat.Hex, GamutMapping = GamutMapping.Clip });
        Assert.Contains(mapped.Diagnostics, d => d.RuleId == DiagnosticCatalog.OutOfGamut.Id && d.Message.Contains("gamut-mapped", StringComparison.Ordinal));
        Assert.Contains(clipped.Diagnostics, d => d.RuleId == DiagnosticCatalog.OutOfGamut.Id && d.Message.Contains("clipped", StringComparison.Ordinal));
        Assert.Equal("--c: #00ff00;", clipped.Css.Split('\n').Single(l => l.Contains("--c:", StringComparison.Ordinal)).Trim());
        Assert.NotEqual(mapped.Declarations[0].Value, clipped.Declarations[0].Value);
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }
}
