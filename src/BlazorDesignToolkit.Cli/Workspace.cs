using System.Globalization;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Bootstrap;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Fluent;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Tailwind;
using BlazorDesignToolkit.Tokens.Validation;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Cli;

/// <summary>A token set to validate: either the merged token files, or one theme/axis combination.</summary>
internal sealed record EvaluationUnit(string? Scope, ThemeSelection? Selection, ResolvedTokenSet Tokens, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Everything loaded from a configuration: tokens or themes, validation options, and helpers to export.</summary>
internal sealed class Workspace
{
    private static readonly Lazy<HttpClient> SharedHttpClient = new(HttpResolverSourceLoader.CreateHttpClient);

    private Workspace(BdtConfig config) => Config = config;

    public BdtConfig Config { get; }
    public ThemeCatalog? Catalog { get; private set; }
    public ResolvedTokenSet? MergedTokens { get; private set; }
    public ValidationOptions Validation { get; private set; } = ValidationOptions.Default;
    public List<Diagnostic> LoadDiagnostics { get; } = [];
    public List<string> InputFiles { get; } = [];

    public bool HasLoadErrors => LoadDiagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    public TokenResolutionOptions ResolutionOptions => new()
    {
        Values = new ValueParseOptions { AllowLegacyValueFormats = Config.AllowLegacyValueFormats, AdditionalDimensionUnits = Config.AdditionalDimensionUnits },
    };

    public static async Task<Workspace> LoadAsync(BdtConfig config, CancellationToken cancellationToken)
    {
        var ws = new Workspace(config);
        var bag = new DiagnosticBag();

        // Validation rules (inline object or separate file); the config-level policy always wins.
        SourceObject? validationRoot = config.ValidationNode;
        if (config.ValidationFile is { } vf)
        {
            var parsed = await SourceJsonParser.ParseFileAsync(config.Resolve(vf), config.Parse, cancellationToken);
            bag.AddRange(parsed.Diagnostics);
            validationRoot = parsed.Root as SourceObject;
            if (parsed.Root is null && !parsed.HasErrors) bag.Report(DiagnosticCatalog.FileNotFound, $"Validation file '{vf}' could not be read.");
        }
        if (validationRoot is not null)
        {
            ws.Validation = ValidationConfig.Read(validationRoot, bag, config.Policy == DiagnosticPolicy.Default ? null : config.Policy);
        }
        else
        {
            ws.Validation = ValidationOptions.Default with { Policy = config.Policy };
        }

        if (config.Themes is { } themes)
        {
            var result = await ThemeManifest.LoadAsync(config.Resolve(themes), config.Parse, config.Remote is { } remote
                ? directory => new HttpResolverSourceLoader(SharedHttpClient.Value, remote, new FileSystemResolverSourceLoader(directory, config.Parse))
                : null, cancellationToken);
            bag.AddRange(result.Diagnostics);
            ws.InputFiles.Add(config.Resolve(themes));
            ws.InputFiles.AddRange(result.Files);
            if (result.Catalog is not null)
            {
                // Re-create the catalog with the configured value options so legacy formats/units apply to themes too.
                var builder = new ThemeCatalogBuilder { ResolutionOptions = ws.ResolutionOptions };
                foreach (var axis in result.Catalog.Axes) builder.AddAxis(axis);
                foreach (var theme in result.Catalog.Themes) builder.AddTheme(theme, theme.Id == result.Catalog.DefaultThemeId);
                ws.Catalog = builder.Build();
            }
        }
        else
        {
            var files = config.Expand(config.Tokens, null, bag);
            if (config.Tokens.Count == 0) bag.Report(DiagnosticCatalog.InvalidConfiguration, "No token files: set \"tokens\" or \"themes\" in the configuration, or pass files on the command line.");
            var layers = new List<SourceObject>();
            foreach (var file in files)
            {
                var parsed = await SourceJsonParser.ParseFileAsync(file, config.Parse with { SourceName = Relative(config, file) }, cancellationToken);
                bag.AddRange(parsed.Diagnostics);
                ws.InputFiles.Add(file);
                if (parsed.Root is SourceObject o) layers.Add(o);
                else if (parsed.Root is not null) bag.Report(DiagnosticCatalog.RootNotObject, $"'{Relative(config, file)}' is not a JSON object.", location: parsed.Root.Location);
            }
            if (layers.Count > 0)
            {
                ws.MergedTokens = TokenResolver.Resolve(DtcgReader.Read(TokenTreeMerger.Merge(layers)), ws.ResolutionOptions, cancellationToken);
            }
        }
        ws.LoadDiagnostics.AddRange(bag.ToSortedList());
        return ws;
    }

    public static string Relative(BdtConfig config, string path) => Path.GetRelativePath(config.BaseDirectory, path).Replace('\\', '/');

    /// <summary>The token sets to validate: every theme × axis combination, or the merged token files.</summary>
    public IEnumerable<EvaluationUnit> Units(CancellationToken cancellationToken)
    {
        if (Catalog is { } catalog)
        {
            foreach (var theme in catalog.Themes)
            {
                foreach (var axes in catalog.EnumerateAxisCombinations())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resolution = catalog.Resolve(new ThemeSelection(theme.Id, axes), cancellationToken: cancellationToken);
                    yield return new EvaluationUnit(ThemeComposer.Scope(resolution.Selection), resolution.Selection, resolution.Tokens, resolution.Diagnostics);
                }
            }
        }
        else if (MergedTokens is { } merged)
        {
            yield return new EvaluationUnit(null, null, merged, merged.Diagnostics);
        }
    }

    /// <summary>The token set used for single-set operations (default theme selection or merged files).</summary>
    public ResolvedTokenSet? Primary(string? theme = null, IReadOnlyDictionary<string, string>? axes = null)
    {
        if (Catalog is { } catalog)
        {
            var selection = new ThemeSelection(theme ?? catalog.DefaultThemeId, axes);
            return catalog.Resolve(selection).Tokens;
        }
        return MergedTokens;
    }

    public static TokenExporterRegistry CreateRegistry()
        => TokenExporterRegistry.CreateDefault()
            .Register(new TailwindTokenExporter())
            .Register(new TailwindConfigTokenExporter())
            .Register(new BootstrapTokenExporter(BootstrapOutput.Scss))
            .Register(new BootstrapTokenExporter(BootstrapOutput.Css))
            .Register(new FluentTokenExporter());

    public static readonly IReadOnlyDictionary<string, string> ThemeCssSettings = new Dictionary<string, string>
    {
        ["prefix"] = "Custom-property prefix.",
        ["layer"] = "Wrap the output in @layer <name>.",
        ["references"] = "'preserve' (default) or 'resolve'.",
        ["colorFormat"] = "'auto' (default), 'hex' or 'native'.",
        ["gamutMapping"] = "'css' (default) or 'clip' for colors outside sRGB in hex output.",
        ["themeAttribute"] = "Attribute carrying the theme id. Default 'data-theme'.",
        ["axisAttributePrefix"] = "Prefix of axis attributes. Default 'data-'.",
        ["systemMediaQueries"] = "Emit @media variants for the 'system' value. Default true.",
        ["colorScheme"] = "Emit the color-scheme property for the mode axis. Default true.",
        ["comments"] = "Emit token descriptions as comments. Default false.",
    };

    public static readonly IReadOnlyDictionary<string, string> BootstrapThemesSettings = new Dictionary<string, string>
    {
        ["prefix"] = "Prefix of the runtime token variables referenced in reference mode.",
        ["values"] = "'reference' (default) or 'literal'.",
        ["themeAttribute"] = "Attribute carrying the theme id. Default 'data-theme'.",
        ["axisAttributePrefix"] = "Prefix of axis attributes. Default 'data-'.",
        ["systemMediaQueries"] = "Emit @media variants for the 'system' value. Default true.",
        ["darkAxis"] = "Axis whose value selects Bootstrap's dark variants. Default 'mode'.",
        ["darkValue"] = "Value of that axis meaning dark. Default 'dark'.",
        ["componentBridge"] = "Append rules that make compiled components (.btn-*, …) read the runtime variables. Default true.",
    };

    /// <summary>Generates one configured output. Returns null content when generation is impossible.</summary>
    public (string? Content, IReadOnlyList<Diagnostic> Diagnostics) Generate(OutputConfig output, TokenExporterRegistry registry, CancellationToken cancellationToken)
    {
        var bag = new DiagnosticBag();
        try
        {
            if (string.Equals(output.Format, "theme-css", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in output.Settings.Keys.Where(k => !ThemeCssSettings.ContainsKey(k)))
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown setting '{key}' for the 'theme-css' exporter.", actual: key, location: output.Location);
                if (Catalog is null)
                {
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, "The 'theme-css' output requires a theme manifest (\"themes\").", location: output.Location);
                    return (null, bag.ToSortedList());
                }
                var context = new TokenExportContext(Catalog.Resolve(Catalog.DefaultSelection).Tokens, output.Settings);
                var css = CssTokenExporter.ReadOptions(context) with { IncludeHeader = false, References = context.GetEnum("references", CssReferenceMode.Preserve) };
                var options = new ThemeCssOptions
                {
                    Css = css,
                    Layer = context.Get("layer"),
                    ThemeAttribute = context.Get("themeAttribute") ?? "data-theme",
                    AxisAttributePrefix = context.Get("axisAttributePrefix") ?? "data-",
                    IncludeSystemMediaQueries = context.GetBool("systemMediaQueries", true),
                    EmitColorScheme = context.GetBool("colorScheme", true),
                };
                var result = ThemeStylesheetGenerator.Generate(Catalog, options, cancellationToken);
                bag.AddRange(result.Diagnostics);
                return (result.Css, bag.ToSortedList());
            }

            if (string.Equals(output.Format, "bootstrap-themes", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in output.Settings.Keys.Where(k => !BootstrapThemesSettings.ContainsKey(k)))
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown setting '{key}' for the 'bootstrap-themes' exporter.", actual: key, location: output.Location);
                if (Catalog is null)
                {
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, "The 'bootstrap-themes' output requires a theme manifest (\"themes\").", location: output.Location);
                    return (null, bag.ToSortedList());
                }
                var context = new TokenExportContext(Catalog.Resolve(Catalog.DefaultSelection).Tokens, output.Settings);
                var mappings = IntegrationSupport.ReadTargetMappings(output.Configuration, bag);
                if (mappings.Count == 0) bag.Report(DiagnosticCatalog.InvalidConfiguration, "The 'bootstrap-themes' output needs a \"mappings\" object in its configuration.", location: output.Location);
                var options = new BootstrapExportOptions
                {
                    Mappings = mappings,
                    Output = BootstrapOutput.Css,
                    Values = context.GetEnum("values", IntegrationValueMode.Reference),
                    Css = CssExportOptions.Default with { Prefix = context.Get("prefix") ?? "" },
                    DarkAxis = context.Get("darkAxis") ?? "mode",
                    DarkValue = context.Get("darkValue") ?? "dark",
                    IncludeComponentBridge = context.GetBool("componentBridge", true),
                };
                var theme = ThemeCssOptions.Default with
                {
                    ThemeAttribute = context.Get("themeAttribute") ?? "data-theme",
                    AxisAttributePrefix = context.Get("axisAttributePrefix") ?? "data-",
                    IncludeSystemMediaQueries = context.GetBool("systemMediaQueries", true),
                };
                var themed = BootstrapRuntime.ExportThemes(Catalog, options, theme);
                bag.AddRange(themed.Diagnostics);
                return (themed.Content, bag.ToSortedList());
            }

            var exporter = registry.Find(output.Format);
            if (exporter is null)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown output format '{output.Format}'.", location: output.Location,
                    expected: string.Join(", ", registry.All.Select(static e => e.Format).Prepend("bootstrap-themes").Prepend("theme-css")), actual: output.Format);
                return (null, bag.ToSortedList());
            }
            bag.AddRange(TokenExporterRegistry.ValidateSettings(exporter, output.Settings));
            var tokens = Primary(output.Theme, output.Axes);
            if (tokens is null)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, "No tokens are available to export.", location: output.Location);
                return (null, bag.ToSortedList());
            }
            var produced = exporter.Export(new TokenExportContext(tokens, output.Settings, cancellationToken, output.Configuration));
            bag.AddRange(produced.Diagnostics);
            return (produced.Content, bag.ToSortedList());
        }
        catch (FormatException ex)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Invalid setting for '{output.Format}' output: {ex.Message}", location: output.Location);
            return (null, bag.ToSortedList());
        }
    }

    internal static string Describe(TokenValue? value) => value?.ToSourceNode().ToJson() ?? "";

    internal static string Percent(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
