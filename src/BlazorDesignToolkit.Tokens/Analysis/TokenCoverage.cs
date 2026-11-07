using System.Text.RegularExpressions;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens.Analysis;

/// <summary>A source file to scan for token usage.</summary>
public sealed record SourceText(string Path, string Content);

/// <summary>Associates a component with the tokens it uses or requires (the component-token contract).</summary>
public sealed record ComponentManifest
{
    public required string Name { get; init; }
    /// <summary>Tokens the component may use (optional). Usage outside this list is reported as BDT8006.</summary>
    public IReadOnlyList<string> Tokens { get; init; } = [];
    /// <summary>Tokens that must exist for the component to render correctly (BDT8003 when missing).</summary>
    public IReadOnlyList<string> RequiredTokens { get; init; } = [];
    /// <summary>Source path globs belonging to the component ('*' within a segment, '**' across segments). Matched against '/'-separated paths.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    public static IReadOnlyList<ComponentManifest> ReadMany(SourceNode node, DiagnosticBag bag)
    {
        var list = new List<ComponentManifest>();
        var components = node is SourceObject root ? root["components"] : node;
        if (components is not SourceArray arr)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "A component manifest must contain a \"components\" array.", location: node.Location);
            return list;
        }
        foreach (var item in arr.Items)
        {
            if (item is not SourceObject o || o["name"] is not SourceString name)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, "Each component manifest entry needs a \"name\".", location: item.Location);
                continue;
            }
            list.Add(new ComponentManifest
            {
                Name = name.Value,
                Tokens = Strings(o["tokens"]),
                RequiredTokens = Strings(o["requiredTokens"]),
                Sources = Strings(o["sources"]),
            });
        }
        return list;
    }

    private static IReadOnlyList<string> Strings(SourceNode? node)
        => node is SourceArray a ? a.Items.OfType<SourceString>().Select(static s => s.Value).ToList() : [];
}

public sealed record CoverageOptions
{
    /// <summary>Naming options used to map custom properties back to tokens; must match the CSS export configuration.</summary>
    public CssExportOptions Css { get; init; } = CssExportOptions.Default;
    public IReadOnlyList<ComponentManifest> Components { get; init; } = [];
    /// <summary>Token patterns that count as used even without source usage (public API of a design system).</summary>
    public IReadOnlyList<string> EntryPoints { get; init; } = [];
}

public sealed record TokenUsage(TokenPath Token, string File, int Line, string Variable, string? Component);

public sealed record ComponentCoverage(string Name, IReadOnlyList<string> UsedTokens, IReadOnlyList<string> MissingRequired, IReadOnlyList<string> Undeclared, IReadOnlyList<string> Files);

public sealed class CoverageReport
{
    public required int TokenCount { get; init; }
    public required int UsedTokenCount { get; init; }
    public required int DirectlyUsedTokenCount { get; init; }
    public required int ScannedFileCount { get; init; }
    public required IReadOnlyList<TokenUsage> Usages { get; init; }
    public required IReadOnlyList<string> UnusedTokens { get; init; }
    public required IReadOnlyList<string> UndefinedVariables { get; init; }
    public required IReadOnlyList<ComponentCoverage> Components { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    /// <summary>Plain-language statements about what this analysis cannot see.</summary>
    public required IReadOnlyList<string> Limitations { get; init; }
    public double CoveragePercent => TokenCount == 0 ? 100 : Math.Round(100.0 * UsedTokenCount / TokenCount, 1);
}

/// <summary>
/// Static analysis of token usage: which CSS custom properties generated from tokens are used in which files and components,
/// which tokens are unused (directly or transitively), which prefixed custom properties are undefined, and whether component
/// contracts are satisfied. Usage that cannot be analysed statically is reported as a limitation, never guessed.
/// </summary>
public static partial class TokenCoverageAnalyzer
{
    public static CoverageReport Analyze(ResolvedTokenSet tokens, IEnumerable<SourceText> sources, CoverageOptions? options = null)
    {
        options ??= new CoverageOptions();
        var bag = new DiagnosticBag();
        var nameMap = new Dictionary<string, TokenPath>(StringComparer.Ordinal);
        foreach (var d in CssExporter.GetDeclarations(tokens, options.Css with { References = CssReferenceMode.Resolve }, new DiagnosticBag()))
        {
            nameMap.TryAdd(d.Name, d.Token);
        }
        var prefix = "--" + (string.IsNullOrEmpty(options.Css.Prefix) ? "" : options.Css.Prefix + "-");
        var usages = new List<TokenUsage>();
        var undefined = new SortedSet<string>(StringComparer.Ordinal);
        var files = 0;
        var componentFiles = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var source in sources.OrderBy(static s => s.Path, StringComparer.Ordinal))
        {
            files++;
            var path = source.Path.Replace('\\', '/');
            var component = options.Components.FirstOrDefault(c => c.Sources.Any(g => GlobMatch(g, path)))?.Name ?? InferComponent(path);
            if (component is not null)
            {
                if (!componentFiles.TryGetValue(component, out var set)) componentFiles[component] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(path);
            }
            var localDefinitions = LocalDefinition().Matches(source.Content).Select(static m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var lineStarts = LineStarts(source.Content);
            foreach (Match m in VarUsage().Matches(source.Content))
            {
                var name = m.Groups[1].Value;
                var line = LineOf(lineStarts, m.Index);
                var tail = source.Content.AsSpan(m.Groups[1].Index + name.Length);
                if (name.EndsWith('-') || (tail.Length > 0 && tail[0] is '@' or '{' or '$' or '+'))
                {
                    bag.Report(DiagnosticCatalog.AnalysisLimit, $"Dynamically built custom property '{name}…' in {path}:{line} cannot be analysed statically.",
                        location: new SourceLocation(path, line, 1), component: component);
                    continue;
                }
                if (nameMap.TryGetValue(name, out var token))
                {
                    usages.Add(new TokenUsage(token, path, line, name, component));
                }
                else if (name.StartsWith(prefix, StringComparison.Ordinal) && prefix.Length > 2 && !localDefinitions.Contains(name))
                {
                    if (undefined.Add(name))
                    {
                        bag.Report(DiagnosticCatalog.UndefinedCustomProperty, $"{path}:{line} uses {name}, which no token defines.",
                            location: new SourceLocation(path, line, 1), actual: name, component: component,
                            remediation: Suggest(name, nameMap.Keys) is { } s ? $"Did you mean {s}?" : "Define the token or remove the usage.");
                    }
                }
            }
        }

        // Transitive usage: a token used directly keeps everything it references alive.
        var used = new HashSet<TokenPath>(usages.Select(static u => u.Token));
        foreach (var t in tokens.Tokens.Where(t => options.EntryPoints.Any(p => TokenPathPattern.IsMatch(p, t.Path.ToString())))) used.Add(t.Path);
        var direct = used.Count;
        var queue = new Queue<TokenPath>(used);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var reference in tokens[current]?.References ?? [])
            {
                if (used.Add(reference)) queue.Enqueue(reference);
            }
        }
        var unused = tokens.Tokens.Where(t => !used.Contains(t.Path)).Select(static t => t.Path.ToString()).ToList();
        foreach (var path in unused)
        {
            bag.Report(DiagnosticCatalog.UnusedToken, $"'{path}' is not used in the analysed sources and no used token references it.", path, tokens[TokenPath.Parse(path)]?.Location);
        }

        var components = new List<ComponentCoverage>();
        foreach (var manifest in options.Components.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            var missing = new List<string>();
            foreach (var required in manifest.RequiredTokens)
            {
                if (tokens.Contains(TokenPath.Parse(required))) continue;
                missing.Add(required);
                bag.Report(DiagnosticCatalog.MissingContractToken, $"Component '{manifest.Name}' requires '{required}', which is not defined.", required, component: manifest.Name);
            }
            foreach (var optional in manifest.Tokens.Where(t => !tokens.Contains(TokenPath.Parse(t))))
            {
                bag.Report(DiagnosticCatalog.UnknownManifestToken, $"Component '{manifest.Name}' lists '{optional}', which is not defined.", optional, component: manifest.Name);
            }
            var declared = manifest.Tokens.Concat(manifest.RequiredTokens).ToHashSet(StringComparer.Ordinal);
            var componentUsed = usages.Where(u => u.Component == manifest.Name).Select(static u => u.Token.ToString()).Distinct().Order(StringComparer.Ordinal).ToList();
            var undeclared = declared.Count == 0 ? [] : componentUsed.Where(u => !declared.Contains(u)).ToList();
            foreach (var u in undeclared)
            {
                bag.Report(DiagnosticCatalog.UndeclaredTokenUsage, $"Component '{manifest.Name}' uses '{u}', which its manifest does not declare.", u, component: manifest.Name);
            }
            components.Add(new ComponentCoverage(manifest.Name, componentUsed, missing, undeclared,
                componentFiles.TryGetValue(manifest.Name, out var f) ? [.. f] : []));
        }
        foreach (var (name, fileSet) in componentFiles.Where(kv => options.Components.All(c => c.Name != kv.Key)).OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            var componentUsed = usages.Where(u => u.Component == name).Select(static u => u.Token.ToString()).Distinct().Order(StringComparer.Ordinal).ToList();
            if (componentUsed.Count > 0) components.Add(new ComponentCoverage(name, componentUsed, [], [], [.. fileSet]));
        }

        return new CoverageReport
        {
            TokenCount = tokens.Count,
            UsedTokenCount = tokens.Tokens.Count(t => used.Contains(t.Path)),
            DirectlyUsedTokenCount = direct,
            ScannedFileCount = files,
            Usages = usages,
            UnusedTokens = unused,
            UndefinedVariables = [.. undefined],
            Components = components,
            Diagnostics = bag.ToSortedList(),
            Limitations =
            [
                "Only literal var(--name) usages in the scanned files are detected; names built at runtime (string interpolation, CSSOM, inline styles from C#) are reported as limitations or not seen.",
                "Tokens consumed through integrations (Tailwind, Bootstrap, Fluent mappings) count as used only if the generated files or utility usages are part of the scanned sources.",
                "Usage inside third-party CSS that is not scanned is invisible to this analysis.",
            ],
        };
    }

    /// <summary>Component name inferred from file names such as <c>Button.razor</c> or <c>Button.razor.css</c>.</summary>
    private static string? InferComponent(string path)
    {
        var file = path[(path.LastIndexOf('/') + 1)..];
        var i = file.IndexOf(".razor", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? file[..i] : null;
    }

    public static bool GlobMatch(string glob, string path)
    {
        var pattern = "^" + Regex.Escape(glob.Replace('\\', '/')).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$";
        return Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || Regex.IsMatch(path, "(^|/)" + pattern.TrimStart('^'), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static string? Suggest(string name, IEnumerable<string> candidates)
        => candidates.Where(c => Math.Abs(c.Length - name.Length) <= 3).OrderBy(c => Distance(c, name)).ThenBy(static c => c, StringComparer.Ordinal)
            .FirstOrDefault(c => Distance(c, name) <= 3);

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static List<int> LineStarts(string text)
    {
        var list = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') list.Add(i + 1);
        return list;
    }

    private static int LineOf(List<int> starts, int index)
    {
        var line = starts.BinarySearch(index);
        return (line < 0 ? ~line - 1 : line) + 1;
    }

    [GeneratedRegex(@"var\(\s*(--[A-Za-z0-9_\-]+)")]
    private static partial Regex VarUsage();

    [GeneratedRegex(@"(?<![A-Za-z0-9_\-])(--[A-Za-z0-9_\-]+)\s*:")]
    private static partial Regex LocalDefinition();
}
