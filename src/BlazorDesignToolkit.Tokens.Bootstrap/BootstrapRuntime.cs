using System.Globalization;
using System.Text;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Bootstrap;

/// <summary>
/// Bootstrap 5.3's Sass color functions, reproduced exactly: <c>mix</c>, <c>shade-color</c>, <c>tint-color</c>,
/// <c>luminance</c>, <c>contrast-ratio</c> and <c>color-contrast</c>. Used to compute, per theme, the values Bootstrap would
/// otherwise compile from <c>$primary</c> and friends.
/// </summary>
public static class BootstrapColors
{
    public static readonly SrgbColor White = new(1, 1, 1);
    public static readonly SrgbColor Black = new(0, 0, 0);

    /// <summary>Sass <c>mix($color1, $color2, $weight)</c> for opaque colors: <paramref name="weight"/> (0–1) of <paramref name="color1"/>.</summary>
    public static SrgbColor Mix(SrgbColor color1, SrgbColor color2, double weight)
        => new(color1.R * weight + color2.R * (1 - weight), color1.G * weight + color2.G * (1 - weight), color1.B * weight + color2.B * (1 - weight));

    public static SrgbColor Shade(SrgbColor color, double weight) => Mix(Black, color, weight);
    public static SrgbColor Tint(SrgbColor color, double weight) => Mix(White, color, weight);

    /// <summary>Bootstrap's <c>luminance()</c>: channels are 8-bit values linearized with the 0.04045 threshold.</summary>
    public static double Luminance(SrgbColor color)
    {
        static double Channel(double v)
        {
            var c = SrgbColor.To8(v) / 255.0;
            return c < 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double ContrastRatio(SrgbColor background, SrgbColor foreground)
    {
        var l1 = Luminance(background);
        var l2 = Luminance(foreground);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Bootstrap's <c>color-contrast()</c>: the first of light, dark, white, black reaching <paramref name="minRatio"/>, else the best one.</summary>
    public static SrgbColor ColorContrast(SrgbColor background, SrgbColor? light = null, SrgbColor? dark = null, double minRatio = 4.5)
    {
        SrgbColor[] candidates = [light ?? White, dark ?? Black, White, Black];
        var best = candidates[0];
        var bestRatio = 0.0;
        foreach (var candidate in candidates)
        {
            var ratio = ContrastRatio(background, candidate);
            if (ratio >= minRatio) return candidate;
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = candidate;
            }
        }
        return best;
    }

    internal static string Rgb(SrgbColor c) => string.Create(CultureInfo.InvariantCulture, $"{SrgbColor.To8(c.R)}, {SrgbColor.To8(c.G)}, {SrgbColor.To8(c.B)}");
}

/// <summary>
/// Runtime Bootstrap 5.3 variables computed from tokens, plus the "component bridge": rules that make component classes
/// whose colors Bootstrap compiles from Sass (<c>.btn-primary</c>, <c>.btn-outline-*</c>, <c>.text-bg-*</c>, <c>.link-*</c>,
/// active states of nav pills, dropdowns, pagination, list groups, progress bars, form checks, focus rings of form controls,
/// range thumbs and the accordion) read runtime variables instead. Combined with <see cref="ExportThemes"/>, every theme and
/// mode switch updates those components without recompiling Bootstrap.
/// </summary>
public static class BootstrapRuntime
{
    /// <summary>Colors that get derived variables (subtle backgrounds, borders, emphasis text) like Bootstrap's own theme colors.</summary>
    public static readonly string[] ThemeColors = ["primary", "secondary", "success", "info", "warning", "danger", "light", "dark"];
    private static readonly HashSet<string> s_subtleColors = new(StringComparer.Ordinal) { "primary", "secondary", "success", "info", "warning", "danger" };

    // Bootstrap 5.3 defaults ($btn-*-shade-amount / -tint-amount, $link-shade-percentage).
    private const double HoverBgAmount = 0.15, HoverBorderShade = 0.20, HoverBorderTint = 0.10, ActiveBgAmount = 0.20, ActiveBorderShade = 0.25, ActiveBorderTint = 0.10;
    private const double LinkShade = 0.20;

    /// <summary>
    /// Runtime declarations for one token set: mapped <c>--bs-*</c> variables with <c>-rgb</c> companions, and for theme
    /// colors the derived values Bootstrap computes in Sass (emphasis/subtle variables, button states, contrast text).
    /// Shades and tints are <c>color-mix(in srgb, …)</c> expressions over the runtime variable in reference mode, so runtime
    /// overrides of the base color are followed; which of shade or tint applies, and the contrast text color, are computed
    /// from this token set with Bootstrap's own algorithm.
    /// </summary>
    public static IReadOnlyList<CssDeclaration> GetDeclarations(ResolvedTokenSet tokens, BootstrapExportOptions options, bool dark, DiagnosticBag bag)
    {
        var list = new List<CssDeclaration>();
        foreach (var mapping in options.Mappings)
        {
            var name = BootstrapExporter.NormalizeName(mapping.Target);
            if (!BootstrapVariables.All.TryGetValue(name, out var variable))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Target}' is not a known Bootstrap 5.3 variable.", mapping.Token, mapping.Location, actual: mapping.Target);
                continue;
            }
            var token = IntegrationSupport.Require(tokens, mapping, variable.Types, "Bootstrap", bag);
            if (token is null) continue;
            if (variable.Runtime is null)
            {
                bag.Report(DiagnosticCatalog.UnsupportedConversion,
                    $"Bootstrap does not read '{variable.Name}' at runtime; it only exists as a Sass variable.", token.Path.ToString(), token.Location,
                    remediation: "Use the Sass output for this variable.");
                continue;
            }
            var value = IntegrationSupport.Format(token, options.Values, options.Css, bag);
            if (value is null) continue;
            list.Add(new CssDeclaration($"--bs-{variable.Runtime}", value, token.Path));
            if (token.Value is not ColorValue color) continue;
            var srgb = Opaque(color, token, bag, $"--bs-{variable.Runtime}-rgb and derived colors");
            if (srgb is not { } c) continue;
            if (variable.RgbCompanion) list.Add(new CssDeclaration($"--bs-{variable.Runtime}-rgb", BootstrapColors.Rgb(c), token.Path));
            if (Array.IndexOf(ThemeColors, variable.Runtime) >= 0) AddThemeColor(list, variable.Runtime, c, dark, options, token.Path);
        }
        return list;
    }

    private static SrgbColor? Opaque(ColorValue color, ResolvedToken token, DiagnosticBag bag, string what)
    {
        if (color.Alpha < 1)
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion, $"{what} need an opaque color, but '{token.Path}' is translucent; they are left to Bootstrap.",
                token.Path.ToString(), token.Location);
            return null;
        }
        var srgb = color.ToSrgb();
        if (srgb.IsInGamut && !color.HasNoneComponent) return srgb.Clip();
        var mapped = color.ToSrgbGamutMapped();
        bag.Report(DiagnosticCatalog.OutOfGamut, $"'{token.Path}' is outside sRGB; {what} use its CSS Color 4 gamut-mapped value {mapped.ToHex(false)}.",
            token.Path.ToString(), token.Location, actual: color.ColorSpace);
        return mapped;
    }

    private static void AddThemeColor(List<CssDeclaration> list, string name, SrgbColor c, bool dark, BootstrapExportOptions options, TokenPath path)
    {
        var reference = options.Values == IntegrationValueMode.Reference;
        var v = reference ? $"var(--bs-{name})" : c.ToHex(false);
        string Mix(SrgbColor partner, double partnerWeight, SrgbColor literal)
            => reference
                ? string.Create(CultureInfo.InvariantCulture, $"color-mix(in srgb, {v} {Pct(1 - partnerWeight)}, {partner.ToHex(false)})")
                : literal.ToHex(false);
        void Add(string property, string val) => list.Add(new CssDeclaration(property, val, path));

        if (s_subtleColors.Contains(name))
        {
            if (!dark)
            {
                Add($"--bs-{name}-text-emphasis", Mix(BootstrapColors.Black, 0.6, BootstrapColors.Shade(c, 0.6)));
                Add($"--bs-{name}-bg-subtle", Mix(BootstrapColors.White, 0.8, BootstrapColors.Tint(c, 0.8)));
                Add($"--bs-{name}-border-subtle", Mix(BootstrapColors.White, 0.6, BootstrapColors.Tint(c, 0.6)));
            }
            else
            {
                Add($"--bs-{name}-text-emphasis", Mix(BootstrapColors.White, 0.4, BootstrapColors.Tint(c, 0.4)));
                Add($"--bs-{name}-bg-subtle", Mix(BootstrapColors.Black, 0.8, BootstrapColors.Shade(c, 0.8)));
                Add($"--bs-{name}-border-subtle", Mix(BootstrapColors.Black, 0.4, BootstrapColors.Shade(c, 0.4)));
            }
        }

        var contrast = BootstrapColors.ColorContrast(c, options.ContrastLight, options.ContrastDark, options.MinContrastRatio);
        var lightText = contrast == (options.ContrastLight ?? BootstrapColors.White);
        // .btn-light always shades and .btn-dark always tints (Bootstrap's _buttons.scss); other colors follow the text color.
        var shade = name == "light" || (name != "dark" && lightText);
        var partner = shade ? BootstrapColors.Black : BootstrapColors.White;
        SrgbColor Toward(double amount) => shade ? BootstrapColors.Shade(c, amount) : BootstrapColors.Tint(c, amount);

        var hoverBg = Toward(HoverBgAmount);
        var hoverBorder = Toward(shade ? HoverBorderShade : HoverBorderTint);
        var activeBg = Toward(ActiveBgAmount);
        var activeBorder = Toward(shade ? ActiveBorderShade : ActiveBorderTint);
        var prefix = $"--bdt-bs-btn-{name}";
        Add($"--bdt-bs-{name}-contrast", contrast.ToHex(false));
        Add($"{prefix}-color", contrast.ToHex(false));
        Add($"{prefix}-hover-color", BootstrapColors.ColorContrast(hoverBg, options.ContrastLight, options.ContrastDark, options.MinContrastRatio).ToHex(false));
        Add($"{prefix}-hover-bg", Mix(partner, HoverBgAmount, hoverBg));
        Add($"{prefix}-hover-border-color", Mix(partner, shade ? HoverBorderShade : HoverBorderTint, hoverBorder));
        Add($"{prefix}-focus-shadow-rgb", BootstrapColors.Rgb(BootstrapColors.Mix(contrast, c, 0.15)));
        Add($"{prefix}-active-color", BootstrapColors.ColorContrast(activeBg, options.ContrastLight, options.ContrastDark, options.MinContrastRatio).ToHex(false));
        Add($"{prefix}-active-bg", Mix(partner, ActiveBgAmount, activeBg));
        Add($"{prefix}-active-border-color", Mix(partner, shade ? ActiveBorderShade : ActiveBorderTint, activeBorder));
        var linkHover = lightText ? BootstrapColors.Shade(c, LinkShade) : BootstrapColors.Tint(c, LinkShade);
        Add($"--bdt-bs-link-{name}-hover-rgb", BootstrapColors.Rgb(linkHover));
    }

    private static string Pct(double fraction) => (Math.Round(fraction * 100, 4)).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Static rules that point Bootstrap's compiled component variables at the runtime variables produced by
    /// <see cref="GetDeclarations"/>. Load after Bootstrap's CSS. Only colors present in <paramref name="colors"/> get rules.
    /// </summary>
    public static string BridgeCss(IEnumerable<string> colors)
    {
        var set = colors.ToHashSet(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("/* Component bridge: compiled Bootstrap component colors read the runtime variables above. Load after bootstrap.css. */\n");
        foreach (var name in ThemeColors.Where(set.Contains))
        {
            var p = $"--bdt-bs-btn-{name}";
            sb.Append($".btn-{name} {{\n");
            sb.Append($"  --bs-btn-color: var({p}-color);\n  --bs-btn-bg: var(--bs-{name});\n  --bs-btn-border-color: var(--bs-{name});\n");
            sb.Append($"  --bs-btn-hover-color: var({p}-hover-color);\n  --bs-btn-hover-bg: var({p}-hover-bg);\n  --bs-btn-hover-border-color: var({p}-hover-border-color);\n");
            sb.Append($"  --bs-btn-focus-shadow-rgb: var({p}-focus-shadow-rgb);\n");
            sb.Append($"  --bs-btn-active-color: var({p}-active-color);\n  --bs-btn-active-bg: var({p}-active-bg);\n  --bs-btn-active-border-color: var({p}-active-border-color);\n");
            sb.Append($"  --bs-btn-disabled-color: var({p}-color);\n  --bs-btn-disabled-bg: var(--bs-{name});\n  --bs-btn-disabled-border-color: var(--bs-{name});\n}}\n");
            sb.Append($".btn-outline-{name} {{\n");
            sb.Append($"  --bs-btn-color: var(--bs-{name});\n  --bs-btn-border-color: var(--bs-{name});\n");
            sb.Append($"  --bs-btn-hover-color: var(--bdt-bs-{name}-contrast);\n  --bs-btn-hover-bg: var(--bs-{name});\n  --bs-btn-hover-border-color: var(--bs-{name});\n");
            sb.Append($"  --bs-btn-focus-shadow-rgb: var(--bs-{name}-rgb);\n");
            sb.Append($"  --bs-btn-active-color: var(--bdt-bs-{name}-contrast);\n  --bs-btn-active-bg: var(--bs-{name});\n  --bs-btn-active-border-color: var(--bs-{name});\n");
            sb.Append($"  --bs-btn-disabled-color: var(--bs-{name});\n  --bs-btn-disabled-border-color: var(--bs-{name});\n}}\n");
            sb.Append($".text-bg-{name} {{ color: var(--bdt-bs-{name}-contrast) !important; }}\n");
            sb.Append($".link-{name}:hover, .link-{name}:focus {{\n");
            sb.Append($"  color: RGBA(var(--bdt-bs-link-{name}-hover-rgb), var(--bs-link-opacity, 1)) !important;\n");
            sb.Append($"  text-decoration-color: RGBA(var(--bdt-bs-link-{name}-hover-rgb), var(--bs-link-underline-opacity, 1)) !important;\n}}\n");
        }
        if (set.Contains("primary"))
        {
            // $component-active-bg defaults to $primary; these components compile it into their own variables or rules.
            sb.Append(".nav-pills { --bs-nav-pills-link-active-bg: var(--bs-primary); }\n");
            sb.Append(".dropdown-menu { --bs-dropdown-link-active-bg: var(--bs-primary); }\n");
            sb.Append(".pagination {\n  --bs-pagination-active-bg: var(--bs-primary);\n  --bs-pagination-active-border-color: var(--bs-primary);\n");
            sb.Append("  --bs-pagination-focus-box-shadow: 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25);\n}\n");
            sb.Append(".list-group {\n  --bs-list-group-active-bg: var(--bs-primary);\n  --bs-list-group-active-border-color: var(--bs-primary);\n}\n");
            sb.Append(".progress, .progress-stacked { --bs-progress-bar-bg: var(--bs-primary); }\n");
            sb.Append(".accordion {\n  --bs-accordion-active-color: var(--bs-primary-text-emphasis);\n  --bs-accordion-active-bg: var(--bs-primary-bg-subtle);\n");
            sb.Append("  --bs-accordion-btn-focus-box-shadow: 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25);\n}\n");
            sb.Append(".form-check-input:checked, .form-check-input[type=checkbox]:indeterminate { background-color: var(--bs-primary); border-color: var(--bs-primary); }\n");
            sb.Append(".form-control:focus, .form-select:focus, .form-check-input:focus {\n");
            sb.Append("  border-color: color-mix(in srgb, var(--bs-primary) 50%, #ffffff);\n  box-shadow: 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25);\n}\n");
            sb.Append(".form-range:focus::-webkit-slider-thumb, .form-range:focus::-moz-range-thumb { box-shadow: 0 0 0 1px var(--bs-body-bg), 0 0 0 0.25rem rgba(var(--bs-primary-rgb), 0.25); }\n");
            sb.Append(".form-range::-webkit-slider-thumb { background-color: var(--bs-primary); }\n");
            sb.Append(".form-range::-moz-range-thumb { background-color: var(--bs-primary); }\n");
            sb.Append(".form-range::-webkit-slider-thumb:active { background-color: color-mix(in srgb, var(--bs-primary) 30%, #ffffff); }\n");
            sb.Append(".form-range::-moz-range-thumb:active { background-color: color-mix(in srgb, var(--bs-primary) 30%, #ffffff); }\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// One stylesheet with the runtime Bootstrap variables of every theme and axis combination (same attributes and cascade
    /// as the toolkit's theme stylesheet, at elevated specificity so they win over Bootstrap's own <c>:root</c> and
    /// <c>[data-bs-theme]</c> rules), followed by the component bridge. A selection is treated as dark when its
    /// <see cref="BootstrapExportOptions.DarkAxis"/> has the value <see cref="BootstrapExportOptions.DarkValue"/>.
    /// </summary>
    public static TokenExportOutput ExportThemes(ThemeCatalog catalog, BootstrapExportOptions options, ThemeCssOptions? theme = null)
    {
        var themeOptions = (theme ?? ThemeCssOptions.Default) with
        {
            ElevateSpecificity = true,
            EmitColorScheme = false,
            IncludeHeader = false,
            Declarations = (selection, tokens, bag) => GetDeclarations(tokens, options, selection[options.DarkAxis] == options.DarkValue, bag),
        };
        var result = ThemeStylesheetGenerator.Generate(catalog, themeOptions);
        var colors = options.Mappings.Select(m => BootstrapExporter.NormalizeName(m.Target)).Where(n => BootstrapVariables.All.TryGetValue(n, out var v) && v.Runtime is not null)
            .Select(n => BootstrapVariables.All[n].Runtime!);
        var sb = new StringBuilder();
        sb.Append("/* Generated by BlazorDesignToolkit for Bootstrap 5.3: runtime variables for every theme and mode. Do not edit by hand. */\n");
        sb.Append(result.Css);
        if (options.IncludeComponentBridge) sb.Append(BridgeCss(colors));
        return new TokenExportOutput(sb.ToString(), result.Diagnostics);
    }
}
