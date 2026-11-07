using System.Text;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Fluent;

/// <summary>
/// Fluent 2 design-token names (as exposed by <c>@fluentui/tokens</c> 1.x and consumed as CSS custom properties by
/// Fluent UI Web Components 3.x and the Fluent UI Blazor library built on them) with the token types each one accepts.
/// The catalog covers the alias tokens most design systems map; names outside it are rejected rather than guessed.
/// </summary>
public static class FluentTokenCatalog
{
    public static IReadOnlyDictionary<string, string[]> Tokens { get; } = Build();

    private static Dictionary<string, string[]> Build()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string type, params string[] names) { foreach (var n in names) map[n] = [type]; }

        Add(TokenTypes.Color,
            "colorNeutralForeground1", "colorNeutralForeground2", "colorNeutralForeground3", "colorNeutralForeground4",
            "colorNeutralForegroundDisabled", "colorNeutralForegroundInverted", "colorNeutralForegroundOnBrand",
            "colorBrandForeground1", "colorBrandForeground2", "colorBrandForegroundLink", "colorBrandForegroundLinkHover",
            "colorNeutralBackground1", "colorNeutralBackground1Hover", "colorNeutralBackground1Pressed", "colorNeutralBackground1Selected",
            "colorNeutralBackground2", "colorNeutralBackground3", "colorNeutralBackground4", "colorNeutralBackground5", "colorNeutralBackground6",
            "colorNeutralBackgroundInverted", "colorNeutralBackgroundDisabled", "colorSubtleBackground", "colorSubtleBackgroundHover",
            "colorTransparentBackground", "colorBackgroundOverlay",
            "colorBrandBackground", "colorBrandBackgroundHover", "colorBrandBackgroundPressed", "colorBrandBackgroundSelected", "colorBrandBackground2",
            "colorNeutralStroke1", "colorNeutralStroke2", "colorNeutralStroke3", "colorNeutralStrokeAccessible", "colorNeutralStrokeDisabled",
            "colorBrandStroke1", "colorBrandStroke2", "colorStrokeFocus1", "colorStrokeFocus2",
            "colorStatusSuccessBackground1", "colorStatusSuccessForeground1", "colorStatusSuccessBorder1",
            "colorStatusWarningBackground1", "colorStatusWarningForeground1", "colorStatusWarningBorder1",
            "colorStatusDangerBackground1", "colorStatusDangerForeground1", "colorStatusDangerBorder1",
            "colorNeutralShadowAmbient", "colorNeutralShadowKey");
        Add(TokenTypes.Dimension,
            "borderRadiusNone", "borderRadiusSmall", "borderRadiusMedium", "borderRadiusLarge", "borderRadiusXLarge", "borderRadiusCircular",
            "strokeWidthThin", "strokeWidthThick", "strokeWidthThicker", "strokeWidthThickest",
            "fontSizeBase100", "fontSizeBase200", "fontSizeBase300", "fontSizeBase400", "fontSizeBase500", "fontSizeBase600",
            "fontSizeHero700", "fontSizeHero800", "fontSizeHero900", "fontSizeHero1000",
            // Fluent line heights are lengths (e.g. 20px), not unitless multipliers.
            "lineHeightBase100", "lineHeightBase200", "lineHeightBase300", "lineHeightBase400", "lineHeightBase500", "lineHeightBase600",
            "lineHeightHero700", "lineHeightHero800", "lineHeightHero900", "lineHeightHero1000");
        foreach (var axis in new[] { "Horizontal", "Vertical" })
        {
            foreach (var size in new[] { "None", "XXS", "XS", "SNudge", "S", "MNudge", "M", "L", "XL", "XXL", "XXXL" })
                map[$"spacing{axis}{size}"] = [TokenTypes.Dimension];
        }
        Add(TokenTypes.FontFamily, "fontFamilyBase", "fontFamilyMonospace", "fontFamilyNumeric");
        Add(TokenTypes.FontWeight, "fontWeightRegular", "fontWeightMedium", "fontWeightSemibold", "fontWeightBold");
        Add(TokenTypes.Duration, "durationUltraFast", "durationFaster", "durationFast", "durationNormal", "durationGentle", "durationSlow", "durationSlower", "durationUltraSlow");
        Add(TokenTypes.CubicBezier, "curveAccelerateMax", "curveAccelerateMid", "curveAccelerateMin", "curveDecelerateMax", "curveDecelerateMid",
            "curveDecelerateMin", "curveEasyEaseMax", "curveEasyEase", "curveLinear");
        Add(TokenTypes.Shadow, "shadow2", "shadow4", "shadow8", "shadow16", "shadow28", "shadow64", "shadow2Brand", "shadow4Brand", "shadow8Brand",
            "shadow16Brand", "shadow28Brand", "shadow64Brand");
        return map;
    }
}

public sealed record FluentExportOptions
{
    public IReadOnlyList<TargetMapping> Mappings { get; init; } = [];
    public IntegrationValueMode Values { get; init; } = IntegrationValueMode.Reference;
    public CssExportOptions Css { get; init; } = CssExportOptions.Default;
    public string Selector { get; init; } = ":root";
}

/// <summary>
/// Maps tokens to Fluent 2 design-token custom properties (for example <c>--colorBrandBackground</c>). Every target name
/// must exist in <see cref="FluentTokenCatalog"/> and the mapped token must have the expected type; a DTCG <c>lineHeight</c>
/// multiplier (number) is rejected for Fluent line-height tokens, which are lengths. Load the output after any theme that
/// Fluent applies itself, or do not apply a Fluent theme, so these declarations win.
/// </summary>
public static class FluentExporter
{
    public static TokenExportOutput Export(ResolvedTokenSet tokens, FluentExportOptions options)
    {
        var bag = new DiagnosticBag();
        if (options.Selector.Contains('{') || options.Selector.Contains('}') || options.Selector.Contains('<'))
        {
            bag.Report(DiagnosticCatalog.InvalidIdentifier, $"Invalid selector '{options.Selector}'.", actual: options.Selector);
            return new TokenExportOutput("", bag.ToSortedList());
        }
        var sb = new StringBuilder("/* Generated by BlazorDesignToolkit for Fluent 2 design tokens. Do not edit by hand. */\n");
        sb.Append(options.Selector).Append(" {\n");
        foreach (var mapping in options.Mappings)
        {
            var name = mapping.Target.TrimStart('-');
            if (!FluentTokenCatalog.Tokens.TryGetValue(name, out var types))
            {
                var hint = FluentTokenCatalog.Tokens.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Target}' is not a Fluent 2 token in the supported catalog.", mapping.Token, mapping.Location,
                    actual: mapping.Target, remediation: hint is null ? null : $"Token names are case-sensitive; did you mean '{hint}'?");
                continue;
            }
            var token = IntegrationSupport.Require(tokens, mapping, types, "Fluent", bag);
            if (token is null) continue;
            if (token.Value is DurationValue && options.Values == IntegrationValueMode.Literal)
            {
                // Fluent durations are written in milliseconds.
                sb.Append("  --").Append(name).Append(": ").Append(CssExporter.FormatNumber(((DurationValue)token.Value).Milliseconds)).Append("ms;\n");
                continue;
            }
            var value = IntegrationSupport.Format(token, options.Values, options.Css, bag);
            if (value is not null) sb.Append("  --").Append(name).Append(": ").Append(value).Append(";\n");
        }
        sb.Append("}\n");
        return new TokenExportOutput(sb.ToString(), bag.ToSortedList());
    }
}

/// <summary><see cref="ITokenExporter"/> adapter (<c>fluent</c>). Configuration: <c>{ "mappings": { "colorBrandBackground": "color.surface.accent" } }</c>.</summary>
public sealed class FluentTokenExporter : ITokenExporter
{
    public string Format => "fluent";
    public string Description => "Fluent 2 design-token custom properties from type-checked mappings.";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["prefix"] = "Prefix of the runtime token variables referenced in reference mode.",
        ["values"] = "'reference' (default) or 'literal'.",
        ["selector"] = "Selector of the generated rule. Default ':root'.",
    };

    public TokenExportOutput Export(TokenExportContext context)
    {
        var bag = new DiagnosticBag();
        var mappings = IntegrationSupport.ReadTargetMappings(context.Configuration, bag);
        if (mappings.Count == 0) bag.Report(DiagnosticCatalog.InvalidConfiguration, "The Fluent exporter needs a \"mappings\" object in its configuration.");
        var result = FluentExporter.Export(context.Tokens, new FluentExportOptions
        {
            Mappings = mappings,
            Values = context.GetEnum("values", IntegrationValueMode.Reference),
            Css = CssExportOptions.Default with { Prefix = context.Get("prefix") ?? "" },
            Selector = context.Get("selector") ?? ":root",
        });
        return new TokenExportOutput(result.Content, [.. bag.ToSortedList(), .. result.Diagnostics]);
    }
}
