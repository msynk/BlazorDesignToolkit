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
    /// <summary>Runtime <c>--bs-*</c> custom-property overrides (plus <c>-rgb</c> companions).</summary>
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
}

/// <summary>
/// Bootstrap 5.3 integration. Each mapping targets a known Bootstrap variable and is type-checked. Sass output requires
/// opaque sRGB colors because Bootstrap's Sass color functions (tint/shade/mix) cannot process wide-gamut or translucent
/// colors; such values are reported instead of being silently converted.
/// Limitation: component classes such as <c>.btn-primary</c> compile their colors from Sass; runtime <c>--bs-*</c> overrides
/// change utilities and body styles but not those compiled component colors.
/// </summary>
public static class BootstrapExporter
{
    public static TokenExportOutput Export(ResolvedTokenSet tokens, BootstrapExportOptions options)
    {
        var bag = new DiagnosticBag();
        var sb = new StringBuilder();
        sb.Append(options.Output == BootstrapOutput.Scss
            ? "// Generated by BlazorDesignToolkit for Bootstrap 5.3. Import before Bootstrap's Sass. Do not edit by hand.\n"
            : "/* Generated by BlazorDesignToolkit for Bootstrap 5.3 runtime variables. Do not edit by hand.\n   Note: component colors such as .btn-primary are compiled from Sass and are not affected by these overrides. */\n");
        if (options.Output == BootstrapOutput.Css)
        {
            if (options.Selector.Contains('{') || options.Selector.Contains('}') || options.Selector.Contains('<'))
            {
                bag.Report(DiagnosticCatalog.InvalidIdentifier, $"Invalid selector '{options.Selector}'.", actual: options.Selector);
                return new TokenExportOutput("", bag.ToSortedList());
            }
            sb.Append(options.Selector).Append(" {\n");
        }
        foreach (var mapping in options.Mappings)
        {
            var name = mapping.Target.TrimStart('$');
            if (name.StartsWith("--bs-", StringComparison.Ordinal)) name = name[5..];
            if (!BootstrapVariables.All.TryGetValue(name, out var variable))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Target}' is not a known Bootstrap 5.3 variable.", mapping.Token, mapping.Location, actual: mapping.Target);
                continue;
            }
            var token = IntegrationSupport.Require(tokens, mapping, variable.Types, "Bootstrap", bag);
            if (token is null) continue;
            if (options.Output == BootstrapOutput.Scss) WriteScss(sb, variable, token, options, bag);
            else WriteCss(sb, variable, token, options, bag);
        }
        if (options.Output == BootstrapOutput.Css) sb.Append("}\n");
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
            bag.Report(DiagnosticCatalog.UnsupportedConversion,
                $"'{token.Path}' is outside the sRGB gamut; Bootstrap's Sass color functions need sRGB colors.", token.Path.ToString(), token.Location,
                remediation: "Map an sRGB token, or use the runtime CSS output.");
            return null;
        }
        if (srgb.A < 1)
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion,
                $"'{token.Path}' is translucent; Bootstrap derives tints and shades from opaque theme colors.", token.Path.ToString(), token.Location);
            return null;
        }
        return srgb.ToHex(includeAlpha: false);
    }

    private static void WriteCss(StringBuilder sb, BootstrapVariables.Variable variable, ResolvedToken token, BootstrapExportOptions options, DiagnosticBag bag)
    {
        if (variable.Runtime is null)
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion,
                $"Bootstrap does not read '{variable.Name}' at runtime; it only exists as a Sass variable.", token.Path.ToString(), token.Location,
                remediation: "Use the Sass output for this variable.");
            return;
        }
        var value = IntegrationSupport.Format(token, options.Values, options.Css, bag);
        if (value is null) return;
        sb.Append("  --bs-").Append(variable.Runtime).Append(": ").Append(value).Append(";\n");
        if (!variable.RgbCompanion || token.Value is not ColorValue color) return;
        var srgb = color.ToSrgb();
        if (!srgb.IsInGamut || srgb.A < 1 || color.HasNoneComponent)
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion,
                $"--bs-{variable.Runtime}-rgb needs an opaque sRGB color, but '{token.Path}' is not; utilities using it are left unchanged.", token.Path.ToString(), token.Location);
            return;
        }
        var c = srgb.Clip();
        sb.Append("  --bs-").Append(variable.Runtime).Append("-rgb: ")
          .Append(string.Create(CultureInfo.InvariantCulture, $"{SrgbColor.To8(c.R)}, {SrgbColor.To8(c.G)}, {SrgbColor.To8(c.B)}")).Append(";\n");
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
        });
        return new TokenExportOutput(result.Content, [.. bag.ToSortedList(), .. result.Diagnostics]);
    }
}
