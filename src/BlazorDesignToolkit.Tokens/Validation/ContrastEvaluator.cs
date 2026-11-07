using System.Globalization;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Validation;

/// <summary>Outcome of a single contrast evaluation.</summary>
public sealed record ContrastResult
{
    public required string Foreground { get; init; }
    public required string Background { get; init; }
    public required ContrastKind Kind { get; init; }
    public required ConformanceLevel Level { get; init; }
    /// <summary>The measured ratio, or null when the rendered colors cannot be established.</summary>
    public double? Ratio { get; init; }
    public required double Required { get; init; }
    /// <summary>True/false when determinable; null means "insufficient evidence".</summary>
    public bool? Passed => Ratio is { } r ? r >= Required : null;
    /// <summary>Why no verdict could be given, when <see cref="Ratio"/> is null.</summary>
    public string? IndeterminateReason { get; init; }
    /// <summary>A suggested foreground (hex) that meets the requirement while keeping hue and chroma, when one exists.</summary>
    public string? SuggestedForeground { get; init; }
    public string? EffectiveForegroundHex { get; init; }
    public string? EffectiveBackgroundHex { get; init; }
    public string? Scope { get; init; }

    /// <summary>Ratio formatted with two decimals, truncated (never rounded up across a threshold).</summary>
    public string RatioText => Ratio is { } r ? FormatRatio(r) : "n/a";

    public static string FormatRatio(double ratio)
        => (Math.Floor(ratio * 100) / 100).ToString("0.00", CultureInfo.InvariantCulture) + ":1";
}

/// <summary>
/// Computes WCAG 2.2 contrast between token colors, accounting for alpha compositing, gamut and 'none' components.
/// It never reports a pass or fail when the rendered colors cannot be established.
/// </summary>
public static class ContrastEvaluator
{
    public static double RequiredRatio(ContrastKind kind, ConformanceLevel level) => (kind, level) switch
    {
        (ContrastKind.Text, ConformanceLevel.AA) => 4.5,
        (ContrastKind.Text, ConformanceLevel.AAA) => 7,
        (ContrastKind.LargeText, ConformanceLevel.AA) => 3,
        (ContrastKind.LargeText, ConformanceLevel.AAA) => 4.5,
        _ => 3,
    };

    /// <summary>Evaluates <paramref name="foreground"/> on <paramref name="background"/>, compositing translucent colors.</summary>
    public static ContrastResult Evaluate(string foregroundName, ColorValue foreground, string backgroundName, ColorValue background,
        ContrastKind kind, ConformanceLevel level, ColorValue? backdrop = null)
    {
        var required = RequiredRatio(kind, level);
        ContrastResult Indeterminate(string reason) => new()
        {
            Foreground = foregroundName, Background = backgroundName, Kind = kind, Level = level, Required = required, IndeterminateReason = reason,
        };

        if (foreground.HasNoneComponent || background.HasNoneComponent)
        {
            return Indeterminate("a color uses the 'none' keyword, whose rendering depends on context (for example interpolation)");
        }
        var fg = foreground.ToSrgb();
        var bg = background.ToSrgb();
        if (!fg.IsInGamut || !bg.IsInGamut)
        {
            return Indeterminate($"{(!fg.IsInGamut ? foregroundName : backgroundName)} lies outside the sRGB gamut; the rendered color depends on the display and the browser's gamut mapping");
        }
        if (bg.A < 1)
        {
            if (backdrop is null)
            {
                return Indeterminate($"{backgroundName} is translucent (alpha {bg.A.ToString("0.###", CultureInfo.InvariantCulture)}) and no opaque backdrop is configured");
            }
            var bd = backdrop.ToSrgb();
            if (bd.A < 1 || !bd.IsInGamut || backdrop.HasNoneComponent) return Indeterminate("the configured backdrop is not an opaque in-gamut color");
            bg = Wcag.CompositeOver(bg, bd);
        }
        if (fg.A < 1) fg = Wcag.CompositeOver(fg, bg);
        var ratio = Wcag.ContrastRatio(fg, bg);
        return new ContrastResult
        {
            Foreground = foregroundName,
            Background = backgroundName,
            Kind = kind,
            Level = level,
            Ratio = ratio,
            Required = required,
            EffectiveForegroundHex = fg.ToHex(false),
            EffectiveBackgroundHex = bg.ToHex(false),
            SuggestedForeground = ratio >= required || foreground.Alpha < 1 ? null : Suggest(foreground, bg, required),
        };
    }

    /// <summary>Searches OKLCH lightness (keeping hue and chroma, clipping to sRGB) for the closest foreground that meets <paramref name="required"/>.</summary>
    internal static string? Suggest(ColorValue foreground, SrgbColor background, double required)
    {
        var lch = ColorMath.Convert(foreground.ColorSpace, foreground.Components, ColorSpaces.Oklch);
        var bgLuminance = Wcag.RelativeLuminance(background);
        string? best = null;
        // Try darker and lighter candidates, preferring the smallest lightness change.
        var bestDelta = double.MaxValue;
        foreach (var direction in new[] { -1, 1 })
        {
            for (var step = 1; step <= 100; step++)
            {
                var l = lch[0] + direction * step * 0.01;
                if (l is < 0 or > 1) break;
                var candidate = ColorMath.ToSrgb(ColorSpaces.Oklch, [l, lch[1], lch[2]]).Clip();
                if (Wcag.ContrastRatio(candidate, background) >= required + 0.005)
                {
                    var delta = Math.Abs(l - lch[0]);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        best = candidate.ToHex(false);
                    }
                    break;
                }
            }
        }
        _ = bgLuminance;
        return best;
    }
}
