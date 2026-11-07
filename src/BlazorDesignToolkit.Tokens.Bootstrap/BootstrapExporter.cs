using System.Globalization;
using System.Text;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Bootstrap;

/// <summary>
/// Known Bootstrap 5.3 variables with the token types they accept. Names are written without the Sass '$' or the CSS '--bs-'
/// prefix. <c>Runtime</c> tells whether Bootstrap also reads the value at runtime from a <c>--bs-*</c> custom property
/// (and whether it needs an <c>-rgb</c> companion).
/// </summary>
public static class BootstrapVariables
{
    public sealed record Variable(string Name, string[] Types, bool Sass, string? Runtime, bool RgbCompanion);

    private static readonly string[] Color = [TokenTypes.Color];
    private static readonly string[] Dimension = [TokenTypes.Dimension];

    public static IReadOnlyDictionary<string, Variable> All { get; } = Build();

    private static Dictionary<string, Variable> Build()
    {
        var list = new List<Variable>();
        foreach (var name in new[] { "primary", "secondary", "success", "info", "warning", "danger", "light", "dark" })
            list.Add(new(name, Color, true, name, true));
        foreach (var name in new[] { "blue", "indigo", "purple", "pink", "red", "orange", "yellow", "green", "teal", "cyan", "white", "black" })
            list.Add(new(name, Color, true, name, false));
        for (var i = 100; i <= 900; i += 100) list.Add(new($"gray-{i}", Color, true, $"gray-{i}", false));
        list.Add(new("body-bg", Color, true, "body-bg", true));
        list.Add(new("body-color", Color, true, "body-color", true));
        list.Add(new("body-secondary-color", Color, true, "secondary-color", false));
        list.Add(new("body-secondary-bg", Color, true, "secondary-bg", true));
        list.Add(new("body-tertiary-color", Color, true, "tertiary-color", false));
        list.Add(new("body-tertiary-bg", Color, true, "tertiary-bg", true));
        list.Add(new("body-emphasis-color", Color, true, "emphasis-color", true));
        list.Add(new("border-color", Color, true, "border-color", false));
        list.Add(new("link-color", Color, true, "link-color", true));
        list.Add(new("link-hover-color", Color, true, "link-hover-color", true));
        list.Add(new("focus-ring-color", Color, true, "focus-ring-color", false));
        list.Add(new("component-active-bg", Color, true, null, false));
        list.Add(new("component-active-color", Color, true, null, false));
        foreach (var name in new[] { "body-bg-dark", "body-color-dark", "border-color-dark", "link-color-dark", "link-hover-color-dark" })
            list.Add(new(name, Color, true, null, false));
        foreach (var name in new[] { "border-width", "border-radius", "border-radius-sm", "border-radius-lg", "border-radius-xl", "border-radius-xxl", "focus-ring-width" })
            list.Add(new(name, Dimension, true, name, false));
        foreach (var name in new[] { "spacer", "font-size-sm", "font-size-lg", "h1-font-size", "h2-font-size", "h3-font-size", "h4-font-size", "h5-font-size", "h6-font-size", "input-btn-padding-y", "input-btn-padding-x" })
            list.Add(new(name, Dimension, true, null, false));
        list.Add(new("font-size-base", Dimension, true, "body-font-size", false));
        list.Add(new("font-family-sans-serif", [TokenTypes.FontFamily], true, "font-sans-serif", false));
        list.Add(new("font-family-monospace", [TokenTypes.FontFamily], true, "font-monospace", false));
        list.Add(new("font-family-base", [TokenTypes.FontFamily], true, "body-font-family", false));
        foreach (var name in new[] { "font-weight-light", "font-weight-medium", "font-weight-semibold", "font-weight-bold", "headings-font-weight" })
            list.Add(new(name, [TokenTypes.FontWeight], true, null, false));
        list.Add(new("font-weight-normal", [TokenTypes.FontWeight], true, "body-font-weight", false));
        list.Add(new("line-height-base", [TokenTypes.Number], true, "body-line-height", false));
        list.Add(new("line-height-sm", [TokenTypes.Number], true, null, false));
        list.Add(new("line-height-lg", [TokenTypes.Number], true, null, false));
        list.Add(new("focus-ring-opacity", [TokenTypes.Number], true, null, false));
        foreach (var name in new[] { "box-shadow", "box-shadow-sm", "box-shadow-lg", "box-shadow-inset" })
            list.Add(new(name, [TokenTypes.Shadow], true, name, false));
        return list.ToDictionary(static v => v.Name, StringComparer.Ordinal);
    }
}

public enum BootstrapOutput
{
    /// <summary>Sass variables (<c>$primary: …;</c>) to import before Bootstrap's own Sass. Compile-time; literal values only.</summary>
    Scss,
    /// <summary>Runtime <c>--bs-*</c> custom-property overrides (plus <c>-rgb</c> companions, derived theme-color variables and the component bridge).</summary>
    Css,
}

public sealed record BootstrapExportOptions
{
    public IReadOnlyList<TargetMapping> Mappings { get; init; } = [];
    public BootstrapOutput Output { get; init; } = BootstrapOutput.Scss;
    /// <summary>For CSS output: reference runtime token variables (default) or emit literals.</summary>
    public IntegrationValueMode Values { get; init; } = IntegrationValueMode.Reference;
    public CssExportOptions Css { get; init; } = CssExportOptions.Default;
    /// <summary>Selector for CSS output. Default <c>:root, [data-bs-theme=light]</c>.</summary>
    public string Selector { get; init; } = ":root, [data-bs-theme=light]";
    /// <summary>For CSS output: append the component bridge (see <see cref="BootstrapRuntime.BridgeCss"/>). Default true.</summary>
    public bool IncludeComponentBridge { get; init; } = true;
    /// <summary>
    /// For single-theme CSS output: compute dark-mode variants of the derived variables (Bootstrap's <c>-dark</c> maps) in
    /// <see cref="Selector"/>, for a file that only serves the dark color mode (with a selector such as <c>[data-bs-theme=dark]</c>).
    /// </summary>
    public bool Dark { get; init; }
    /// <summary>
    /// For single-theme CSS output without <see cref="Dark"/>: selector of a second block with the dark-mode variants of the
    /// mode-dependent derived variables (emphasis text, subtle backgrounds and borders), so that the light values do not
    /// override Bootstrap's dark color mode. Default <c>[data-bs-theme=dark]</c>; null or empty to omit the block.
    /// </summary>
    public string? DarkSelector { get; init; } = "[data-bs-theme=dark]";
    /// <summary>Theme axis and value that select Bootstrap's dark variants in <see cref="BootstrapRuntime.ExportThemes"/>. Default <c>mode</c> = <c>dark</c>.</summary>
    public string DarkAxis { get; init; } = "mode";
    public string DarkValue { get; init; } = "dark";
    /// <summary><c>$color-contrast-light</c> / <c>$color-contrast-dark</c> / <c>$min-contrast-ratio</c>, if customized in your Bootstrap build.</summary>
    public SrgbColor? ContrastLight { get; init; }
    public SrgbColor? ContrastDark { get; init; }
    public double MinContrastRatio { get; init; } = 4.5;
}

/// <summary>
/// Bootstrap 5.3 integration. Each mapping targets a known Bootstrap variable and is type-checked. Sass output needs opaque
/// sRGB colors because Bootstrap's Sass color functions (tint/shade/mix) work in sRGB: wide-gamut colors are CSS Color 4
/// gamut-mapped (BDT7003) and translucent colors are rejected. CSS output adds the variables Bootstrap derives from theme
/// colors and a component bridge so that compiled components (<c>.btn-primary</c> …) follow runtime changes; for every
/// theme and mode at once use <see cref="BootstrapRuntime.ExportThemes"/>.
/// </summary>
public static class BootstrapExporter
{
    internal static string NormalizeName(string target)
    {
        var name = target.TrimStart('$');
        return name.StartsWith("--bs-", StringComparison.Ordinal) ? name[5..] : name;
    }

    public static TokenExportOutput Export(ResolvedTokenSet tokens, BootstrapExportOptions options)
    {
        var bag = new DiagnosticBag();
        var sb = new StringBuilder();
        if (options.Output == BootstrapOutput.Css)
        {
            sb.Append("/* Generated by BlazorDesignToolkit for Bootstrap 5.3 runtime variables. Do not edit by hand. */\n");
            foreach (var selector in new[] { options.Selector, options.DarkSelector })
            {
                if (selector is not null && (selector.Contains('{') || selector.Contains('}') || selector.Contains('<')))
                {
                    bag.Report(DiagnosticCatalog.InvalidIdentifier, $"Invalid selector '{selector}'.", actual: selector);
                    return new TokenExportOutput("", bag.ToSortedList());
                }
            }
            var declarations = BootstrapRuntime.GetDeclarations(tokens, options, options.Dark, bag);
            CssExporter.WriteBlock(sb, options.Selector, declarations, null, includeComments: false, indent: "");
            if (!options.Dark && !string.IsNullOrWhiteSpace(options.DarkSelector))
            {
                // Bootstrap redefines the emphasis and subtle variables for its dark color mode under [data-bs-theme=dark].
                // This file loads after bootstrap.css, so the light values above (on :root) would win there; the dark
                // variants follow with the same specificity, as in Bootstrap's own stylesheet.
                var dark = BootstrapRuntime.GetDeclarations(tokens, options, dark: true, new DiagnosticBag())
                    .Where(static d => BootstrapRuntime.IsModeDependent(d.Name)).ToList();
                if (dark.Count > 0) CssExporter.WriteBlock(sb, options.DarkSelector, dark, null, includeComments: false, indent: "");
            }
            if (options.IncludeComponentBridge)
            {
                sb.Append(BootstrapRuntime.BridgeCss(BootstrapRuntime.DerivedColors(declarations)));
            }
            return new TokenExportOutput(sb.ToString(), bag.ToSortedList());
        }
        sb.Append("// Generated by BlazorDesignToolkit for Bootstrap 5.3. Import before Bootstrap's Sass. Do not edit by hand.\n");
        foreach (var mapping in options.Mappings)
        {
            var name = NormalizeName(mapping.Target);
            if (!BootstrapVariables.All.TryGetValue(name, out var variable))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Target}' is not a known Bootstrap 5.3 variable.", mapping.Token, mapping.Location, actual: mapping.Target);
                continue;
            }
            var token = IntegrationSupport.Require(tokens, mapping, variable.Types, "Bootstrap", bag);
            if (token is null) continue;
            WriteScss(sb, variable, token, options, bag);
        }
        return new TokenExportOutput(sb.ToString(), bag.ToSortedList());
    }

    private static void WriteScss(StringBuilder sb, BootstrapVariables.Variable variable, ResolvedToken token, BootstrapExportOptions options, DiagnosticBag bag)
    {
        string? value;
        if (token.Value is ColorValue color)
        {
            value = SassColor(color, token, bag);
        }
        else if (token.Value is FontFamilyValue family)
        {
            value = string.Join(", ", family.Families.Select(static f => IsGeneric(f) ? f : CssExporter.QuoteString(f)));
        }
        else
        {
            value = CssExporter.FormatValue(token.Value!, options.Css with { ColorFormat = CssColorFormat.Hex }, bag, token.Path.ToString());
        }
        if (value is not null) sb.Append('$').Append(variable.Name).Append(": ").Append(value).Append(";\n");
    }

    private static bool IsGeneric(string family) => family is "serif" or "sans-serif" or "monospace" or "system-ui" or "ui-monospace" or "ui-sans-serif" or "ui-serif" or "cursive" or "fantasy";

    private static string? SassColor(ColorValue color, ResolvedToken token, DiagnosticBag bag)
    {
        var srgb = color.ToSrgb();
        if (!srgb.IsInGamut || color.HasNoneComponent)
        {
            var mapped = color.ToSrgbGamutMapped();
            bag.Report(DiagnosticCatalog.OutOfGamut,
                $"'{token.Path}' is outside the sRGB gamut that Bootstrap's Sass color functions work in; its CSS Color 4 gamut-mapped value {mapped.ToHex(false)} is used.",
                token.Path.ToString(), token.Location, actual: color.ColorSpace, remediation: "Map an sRGB token to keep the exact color.");
            srgb = mapped;
        }
        if (srgb.A < 1)
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion,
                $"'{token.Path}' is translucent; Bootstrap derives tints and shades from opaque theme colors.", token.Path.ToString(), token.Location);
            return null;
        }
        return srgb.ToHex(includeAlpha: false);
    }

}

/// <summary><see cref="ITokenExporter"/> adapters: <c>bootstrap-scss</c> and <c>bootstrap-css</c>.</summary>
public sealed class BootstrapTokenExporter(BootstrapOutput output) : ITokenExporter
{
    public string Format => output == BootstrapOutput.Scss ? "bootstrap-scss" : "bootstrap-css";
    public string Description => output == BootstrapOutput.Scss
        ? "Bootstrap 5.3 Sass variables (compile-time) from type-checked mappings."
        : "Bootstrap 5.3 runtime --bs-* custom properties (with -rgb companions) from type-checked mappings.";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["prefix"] = "Prefix of the runtime token variables (CSS output, reference mode).",
        ["values"] = "'reference' (default) or 'literal' (CSS output).",
        ["selector"] = "Selector for CSS output. Default ':root, [data-bs-theme=light]'.",
        ["dark"] = "CSS output: 'true' to compute Bootstrap's dark-mode variants in 'selector' (for a dark-only file). Default false.",
        ["darkSelector"] = "CSS output: selector of the dark-mode variants of the emphasis/subtle variables. Default '[data-bs-theme=dark]'; empty to omit.",
    };

    public TokenExportOutput Export(TokenExportContext context)
    {
        var bag = new DiagnosticBag();
        var mappings = IntegrationSupport.ReadTargetMappings(context.Configuration, bag);
        if (mappings.Count == 0) bag.Report(DiagnosticCatalog.InvalidConfiguration, "The Bootstrap exporter needs a \"mappings\" object in its configuration.");
        var result = BootstrapExporter.Export(context.Tokens, new BootstrapExportOptions
        {
            Mappings = mappings,
            Output = output,
            Values = context.GetEnum("values", IntegrationValueMode.Reference),
            Css = CssExportOptions.Default with { Prefix = context.Get("prefix") ?? "" },
            Selector = context.Get("selector") ?? ":root, [data-bs-theme=light]",
            Dark = context.GetBool("dark", false),
            DarkSelector = context.Get("darkSelector") ?? "[data-bs-theme=dark]",
        });
        return new TokenExportOutput(result.Content, [.. bag.ToSortedList(), .. result.Diagnostics]);
    }
}
