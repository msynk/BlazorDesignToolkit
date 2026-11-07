using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Validation;
using Microsoft.Extensions.FileSystemGlobbing;

namespace BlazorDesignToolkit.Cli;

/// <summary>One configured output (<c>outputs[]</c> in bdt.config.json).</summary>
internal sealed record OutputConfig(string Format, string Path, IReadOnlyDictionary<string, string> Settings, SourceObject? Configuration,
    string? Theme, IReadOnlyDictionary<string, string> Axes, SourceLocation? Location);

internal sealed record CoverageConfig(IReadOnlyList<string> Sources, IReadOnlyList<string> Exclude, IReadOnlyList<string> Manifests,
    IReadOnlyList<string> EntryPoints, string? Prefix, double? MinimumPercent);

/// <summary>The parsed <c>bdt.config.json</c>. All paths are relative to <see cref="BaseDirectory"/>.</summary>
internal sealed class BdtConfig
{
    public const string FileName = "bdt.config.json";

    public required string BaseDirectory { get; init; }
    public string? SourcePath { get; init; }
    public IReadOnlyList<string> Tokens { get; init; } = [];
    public string? Themes { get; init; }
    public JsonParseOptions Parse { get; init; } = JsonParseOptions.Default;
    public bool AllowLegacyValueFormats { get; init; }
    public IReadOnlySet<string> AdditionalDimensionUnits { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public SourceObject? ValidationNode { get; init; }
    public string? ValidationFile { get; init; }
    public DiagnosticPolicy Policy { get; init; } = DiagnosticPolicy.Default;
    public IReadOnlyList<OutputConfig> Outputs { get; init; } = [];
    public CoverageConfig? Coverage { get; init; }

    public string Resolve(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseDirectory, relative));

    /// <summary>Expands globs relative to the base directory; results are sorted ordinally for deterministic merging.</summary>
    public IReadOnlyList<string> Expand(IEnumerable<string> patterns, IEnumerable<string>? exclude, DiagnosticBag bag)
    {
        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                var full = Resolve(pattern);
                if (File.Exists(full)) files.Add(full);
                else bag.Report(DiagnosticCatalog.FileNotFound, $"File '{pattern}' was not found.", actual: full);
                continue;
            }
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern);
            foreach (var ex in exclude ?? []) matcher.AddExclude(ex);
            var matches = matcher.GetResultsInFullPath(BaseDirectory).Order(StringComparer.Ordinal).ToList();
            if (matches.Count == 0) bag.Report(DiagnosticCatalog.FileNotFound, $"Pattern '{pattern}' matched no files.", actual: pattern);
            files.AddRange(matches);
        }
        return files.Distinct(StringComparer.Ordinal).ToList();
    }

    public static async Task<(BdtConfig? Config, IReadOnlyList<Diagnostic> Diagnostics)> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var bag = new DiagnosticBag();
        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            bag.Report(DiagnosticCatalog.FileNotFound, $"Configuration file '{path}' was not found.", actual: full);
            return (null, bag.ToSortedList());
        }
        var parsed = await SourceJsonParser.ParseFileAsync(full, cancellationToken: cancellationToken);
        bag.AddRange(parsed.Diagnostics);
        if (parsed.Root is not SourceObject root)
        {
            if (parsed.Root is not null) bag.Report(DiagnosticCatalog.InvalidConfiguration, "The configuration must be a JSON object.", location: parsed.Root.Location);
            return (null, bag.ToSortedList());
        }
        var config = Read(root, System.IO.Path.GetDirectoryName(full)!, full, bag);
        return (bag.HasErrors ? null : config, bag.ToSortedList());
    }

    public static BdtConfig Read(SourceObject root, string baseDirectory, string? sourcePath, DiagnosticBag bag)
    {
        ValidationConfig.CheckKeys(root, bag, "bdt.config.json", "$schema", "$description", "tokens", "themes", "parse", "validation", "policy", "outputs", "coverage");
        var parse = JsonParseOptions.Default;
        var legacy = false;
        var units = new HashSet<string>(StringComparer.Ordinal);
        if (root["parse"] is SourceObject p)
        {
            ValidationConfig.CheckKeys(p, bag, "parse", "maxInputBytes", "maxDepth", "allowComments", "allowLegacyValueFormats", "additionalDimensionUnits");
            parse = parse with
            {
                MaxInputBytes = p["maxInputBytes"] is SourceNumber mb ? (long)mb.Value : parse.MaxInputBytes,
                MaxDepth = p["maxDepth"] is SourceNumber md ? (int)md.Value : parse.MaxDepth,
                AllowCommentsAndTrailingCommas = p["allowComments"] is SourceBoolean { Value: true },
            };
            legacy = p["allowLegacyValueFormats"] is SourceBoolean { Value: true };
            foreach (var u in ValidationConfig.Strings(p["additionalDimensionUnits"])) units.Add(u);
        }

        var outputs = new List<OutputConfig>();
        if (root["outputs"] is SourceArray outs)
        {
            foreach (var item in outs.Items)
            {
                if (item is not SourceObject o || o["format"] is not SourceString format || o["path"] is not SourceString outPath)
                {
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, "Each output needs \"format\" and \"path\".", location: item.Location);
                    continue;
                }
                ValidationConfig.CheckKeys(o, bag, "output", "format", "path", "settings", "config", "theme", "axes");
                outputs.Add(new OutputConfig(format.Value, outPath.Value, StringMap(o["settings"], bag), o["config"] as SourceObject,
                    (o["theme"] as SourceString)?.Value, StringMap(o["axes"], bag), o.Location));
            }
        }

        CoverageConfig? coverage = null;
        if (root["coverage"] is SourceObject c)
        {
            ValidationConfig.CheckKeys(c, bag, "coverage", "sources", "exclude", "manifests", "entryPoints", "prefix", "minimumPercent");
            coverage = new CoverageConfig(ValidationConfig.Strings(c["sources"]), ValidationConfig.Strings(c["exclude"]), ValidationConfig.Strings(c["manifests"]),
                ValidationConfig.Strings(c["entryPoints"]), (c["prefix"] as SourceString)?.Value, (c["minimumPercent"] as SourceNumber)?.Value);
        }

        return new BdtConfig
        {
            BaseDirectory = baseDirectory,
            SourcePath = sourcePath,
            Tokens = ValidationConfig.Strings(root["tokens"]),
            Themes = (root["themes"] as SourceString)?.Value,
            Parse = parse,
            AllowLegacyValueFormats = legacy,
            AdditionalDimensionUnits = units,
            ValidationNode = root["validation"] as SourceObject,
            ValidationFile = (root["validation"] as SourceString)?.Value,
            Policy = root["policy"] is SourceObject policy ? ValidationConfig.ReadPolicy(policy, bag) : DiagnosticPolicy.Default,
            Outputs = outputs,
            Coverage = coverage,
        };
    }

    private static IReadOnlyDictionary<string, string> StringMap(SourceNode? node, DiagnosticBag bag)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is not SourceObject o) return map;
        foreach (var p in o.Properties)
        {
            map[p.Name] = p.Value switch
            {
                SourceString s => s.Value,
                SourceBoolean b => b.Value ? "true" : "false",
                SourceNumber n => n.ToInvariantString(),
                _ => Invalid(p, bag),
            };
        }
        return map;
    }

    private static string Invalid(SourceProperty p, DiagnosticBag bag)
    {
        bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Setting '{p.Name}' must be a string, number or boolean.", location: p.Value.Location);
        return "";
    }
}
