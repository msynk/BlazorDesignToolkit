using System.Globalization;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens.Export;

/// <summary>Input for an exporter: the resolved tokens plus string settings (from configuration files or code).</summary>
public sealed class TokenExportContext(ResolvedTokenSet tokens, IReadOnlyDictionary<string, string>? settings = null, CancellationToken cancellationToken = default,
    Json.SourceObject? configuration = null)
{
    public ResolvedTokenSet Tokens { get; } = tokens;
    public IReadOnlyDictionary<string, string> Settings { get; } = settings ?? new Dictionary<string, string>();
    public CancellationToken CancellationToken { get; } = cancellationToken;
    /// <summary>Structured exporter configuration (for example mapping tables) from the configuration file, if any.</summary>
    public Json.SourceObject? Configuration { get; } = configuration;

    public string? Get(string key) => Settings.TryGetValue(key, out var v) ? v : null;

    public bool GetBool(string key, bool fallback) => Get(key) is { } v ? bool.Parse(v) : fallback;

    public int GetInt(string key, int fallback) => Get(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public T GetEnum<T>(string key, T fallback) where T : struct, Enum
        => Get(key) is { } v ? (Enum.TryParse<T>(v.Replace("-", ""), ignoreCase: true, out var r) ? r : throw new FormatException($"'{v}' is not a valid value for '{key}'. Expected one of: {string.Join(", ", Enum.GetNames<T>())}.")) : fallback;
}

public sealed class TokenExportOutput(string content, IReadOnlyList<Diagnostic> diagnostics)
{
    public string Content { get; } = content;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Extension point for export targets. Implementations must be deterministic and must not perform I/O or network access.</summary>
public interface ITokenExporter
{
    /// <summary>Format identifier used in configuration, e.g. "css".</summary>
    string Format { get; }
    string Description { get; }
    /// <summary>Recognised setting keys with descriptions (documentation and configuration validation).</summary>
    IReadOnlyDictionary<string, string> SupportedSettings { get; }
    TokenExportOutput Export(TokenExportContext context);
}

/// <summary>Registry of available exporters. Integrations register themselves explicitly; nothing is discovered by reflection.</summary>
public sealed class TokenExporterRegistry
{
    private readonly Dictionary<string, ITokenExporter> _exporters = new(StringComparer.OrdinalIgnoreCase);

    public static TokenExporterRegistry CreateDefault()
    {
        var registry = new TokenExporterRegistry();
        registry.Register(new CssTokenExporter());
        registry.Register(new DtcgJsonTokenExporter());
        return registry;
    }

    public TokenExporterRegistry Register(ITokenExporter exporter)
    {
        _exporters[exporter.Format] = exporter;
        return this;
    }

    public ITokenExporter? Find(string format) => _exporters.GetValueOrDefault(format);

    public IEnumerable<ITokenExporter> All => _exporters.Values.OrderBy(static e => e.Format, StringComparer.Ordinal);

    /// <summary>Reports setting keys an exporter does not recognise (typos in configuration).</summary>
    public static IReadOnlyList<Diagnostic> ValidateSettings(ITokenExporter exporter, IReadOnlyDictionary<string, string> settings)
    {
        var bag = new DiagnosticBag();
        foreach (var key in settings.Keys)
        {
            if (!exporter.SupportedSettings.ContainsKey(key))
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown setting '{key}' for the '{exporter.Format}' exporter.",
                    expected: string.Join(", ", exporter.SupportedSettings.Keys), actual: key);
            }
        }
        return bag.ToSortedList();
    }
}

/// <summary>Plain CSS custom properties for a single token set.</summary>
public sealed class CssTokenExporter : ITokenExporter
{
    public string Format => "css";
    public string Description => "CSS custom properties (one rule, configurable selector, prefix, references and color format).";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["prefix"] = "Custom-property prefix (letters, digits, '-', '_').",
        ["selector"] = "Selector of the generated rule. Default ':root'.",
        ["layer"] = "Wrap the output in @layer <name>.",
        ["references"] = "'resolve' (default) or 'preserve' to emit var() for aliases.",
        ["colorFormat"] = "'auto' (default), 'hex' or 'native'.",
        ["nameStyle"] = "'kebab-case' (default) or 'preserve'.",
        ["expandComposites"] = "Emit typography/border/transition sub-properties. Default true.",
        ["comments"] = "Emit descriptions as comments. Default false.",
        ["ordering"] = "'document' (default) or 'alphabetical'.",
        ["precision"] = "Maximum fractional digits. Default 5.",
    };

    public static CssExportOptions ReadOptions(TokenExportContext context) => new()
    {
        Prefix = context.Get("prefix") ?? "",
        Selector = context.Get("selector") ?? ":root",
        Layer = context.Get("layer"),
        References = context.GetEnum("references", CssReferenceMode.Resolve),
        ColorFormat = context.GetEnum("colorFormat", CssColorFormat.Auto),
        NameStyle = context.GetEnum("nameStyle", CssNameStyle.KebabCase),
        ExpandComposites = context.GetBool("expandComposites", true),
        IncludeComments = context.GetBool("comments", false),
        Ordering = context.GetEnum("ordering", TokenOrdering.Document),
        Precision = context.GetInt("precision", 5),
    };

    public TokenExportOutput Export(TokenExportContext context)
    {
        var result = CssExporter.Export(context.Tokens, ReadOptions(context));
        return new TokenExportOutput(result.Css, result.Diagnostics);
    }
}

/// <summary>Resolved (or reference-preserving) DTCG 2025.10 JSON.</summary>
public sealed class DtcgJsonTokenExporter : ITokenExporter
{
    public string Format => "dtcg-json";
    public string Description => "DTCG 2025.10 JSON with explicit $type on every token; values resolved unless references are preserved.";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["references"] = "'resolve' (default) or 'preserve'.",
        ["ordering"] = "'document' (default) or 'alphabetical'.",
        ["descriptions"] = "Include $description. Default true.",
        ["extensions"] = "Include $extensions. Default true.",
    };

    public TokenExportOutput Export(TokenExportContext context)
    {
        var options = new DtcgWriteOptions
        {
            PreserveReferences = context.GetEnum("references", CssReferenceMode.Resolve) == CssReferenceMode.Preserve,
            Ordering = context.GetEnum("ordering", TokenOrdering.Document),
            IncludeDescriptions = context.GetBool("descriptions", true),
            IncludeExtensions = context.GetBool("extensions", true),
        };
        var bag = new DiagnosticBag();
        foreach (var invalid in context.Tokens.Tokens.Where(static t => !t.IsValid))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion, $"'{invalid.Path}' is invalid and was omitted from the JSON output.", invalid.Path.ToString(), invalid.Location);
        }
        return new TokenExportOutput(DtcgWriter.Write(context.Tokens, options), bag.ToSortedList());
    }
}
