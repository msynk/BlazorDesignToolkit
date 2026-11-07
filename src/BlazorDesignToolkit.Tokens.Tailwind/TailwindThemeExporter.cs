using System.Text;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tailwind;

/// <summary>Maps tokens matching <see cref="Tokens"/> into the Tailwind theme namespace <see cref="Namespace"/>.</summary>
/// <param name="Tokens">Token path pattern ('*' one segment, '**' any), e.g. <c>color.**</c>.</param>
/// <param name="Namespace">Tailwind v4 theme namespace, e.g. <c>color</c>, <c>radius</c>, <c>text</c>.</param>
/// <param name="StripPrefix">Path prefix removed when building the utility name. Defaults to the literal prefix of the pattern.</param>
public sealed record TailwindMapping(string Tokens, string Namespace, string? StripPrefix = null);

public sealed record TailwindExportOptions
{
    public IReadOnlyList<TailwindMapping> Mappings { get; init; } = [];
    public IntegrationValueMode Values { get; init; } = IntegrationValueMode.Reference;
    /// <summary>Naming of the runtime variables referenced in <see cref="IntegrationValueMode.Reference"/> mode. Use a prefix.</summary>
    public CssExportOptions Css { get; init; } = CssExportOptions.Default;
    /// <summary>Clear Tailwind's default values for each mapped namespace (<c>--color-*: initial;</c>).</summary>
    public bool ResetMappedNamespaces { get; init; }
    public bool IncludeHeader { get; init; } = true;
}

/// <summary>
/// Generates a Tailwind CSS v4 <c>@theme</c> block from tokens. Every mapping is type-checked against the namespace
/// (for example <c>radius</c> requires dimensions, <c>shadow</c> requires shadows); mismatches are reported, never coerced.
/// In reference mode the block uses <c>@theme inline</c> so utilities resolve to the runtime theme variables and follow
/// theme and mode switches. Supported: Tailwind CSS 4.0 and later; for Tailwind 3 use <see cref="TailwindConfigExporter"/>.
/// Breakpoint and container sizes are always written as literal values, because Tailwind uses them in media and container
/// queries, where <c>var()</c> is not allowed.
/// </summary>
public static class TailwindThemeExporter
{
    private sealed record NamespaceRule(string Name, string[] Types, string Description);

    /// <summary>Tailwind v4 theme variable namespaces supported by the exporter and the token types they accept.</summary>
    public static IReadOnlyDictionary<string, string[]> Namespaces { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["color"] = [TokenTypes.Color],
        ["font"] = [TokenTypes.FontFamily],
        ["text"] = [TokenTypes.Dimension, TokenTypes.Typography],
        ["font-weight"] = [TokenTypes.FontWeight],
        ["tracking"] = [TokenTypes.Dimension],
        ["leading"] = [TokenTypes.Number, TokenTypes.Dimension],
        ["spacing"] = [TokenTypes.Dimension],
        ["radius"] = [TokenTypes.Dimension],
        ["shadow"] = [TokenTypes.Shadow],
        ["inset-shadow"] = [TokenTypes.Shadow],
        ["drop-shadow"] = [TokenTypes.Shadow],
        ["blur"] = [TokenTypes.Dimension],
        ["perspective"] = [TokenTypes.Dimension],
        ["breakpoint"] = [TokenTypes.Dimension],
        ["container"] = [TokenTypes.Dimension],
        ["ease"] = [TokenTypes.CubicBezier],
    };

    /// <summary>Namespaces whose values Tailwind evaluates at build time inside @media/@container queries.</summary>
    internal static readonly HashSet<string> LiteralOnlyNamespaces = new(StringComparer.Ordinal) { "breakpoint", "container" };

    public static TokenExportOutput Export(ResolvedTokenSet tokens, TailwindExportOptions options)
    {
        var bag = new DiagnosticBag();
        var lines = new List<string>();
        // Theme variable names across all mappings (Match only detects collisions within one mapping).
        var names = new Dictionary<string, TokenPath>(StringComparer.Ordinal);
        if (!CheckPrefix(options, bag)) return new TokenExportOutput("", bag.ToSortedList());
        foreach (var mapping in options.Mappings)
        {
            if (!Namespaces.ContainsKey(mapping.Namespace))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Namespace}' is not a supported Tailwind v4 theme namespace.",
                    expected: string.Join(", ", Namespaces.Keys), actual: mapping.Namespace);
                continue;
            }
            if (options.ResetMappedNamespaces) AddLine(lines, $"--{mapping.Namespace}-*: initial;");
            foreach (var (token, suffix) in Match(tokens, mapping, bag))
            {
                var name = suffix.Length == 0 ? $"--{mapping.Namespace}" : $"--{mapping.Namespace}-{suffix}";
                if (names.TryGetValue(name, out var existing))
                {
                    // The same token mapped twice to the same name is harmless; a different token would silently win.
                    if (existing != token.Path)
                        bag.Report(DiagnosticCatalog.NameCollision, $"'{token.Path}' and '{existing}' both map to Tailwind variable {name}.", token.Path.ToString(), token.Location);
                    continue;
                }
                names[name] = token.Path;
                foreach (var line in Declarations(name, token, mapping.Namespace, options, bag)) AddLine(lines, line);
            }
        }
        var sb = new StringBuilder();
        if (options.IncludeHeader)
        {
            sb.Append("/* Generated by BlazorDesignToolkit for Tailwind CSS v4. Do not edit by hand. */\n");
            if (options.Values == IntegrationValueMode.Reference)
                sb.Append("/* Values reference the runtime token variables; load the token stylesheet before Tailwind's output. */\n");
        }
        sb.Append(options.Values == IntegrationValueMode.Reference ? "@theme inline {\n" : "@theme {\n");
        foreach (var line in lines) sb.Append("  ").Append(line).Append('\n');
        sb.Append("}\n");
        return new TokenExportOutput(sb.ToString(), bag.ToSortedList());
    }

    internal static bool CheckPrefix(TailwindExportOptions options, DiagnosticBag bag)
    {
        if (options.Values != IntegrationValueMode.Reference || !string.IsNullOrEmpty(options.Css.Prefix)) return true;
        bag.Report(DiagnosticCatalog.InvalidConfiguration,
            "Reference mode needs a CSS prefix for the runtime variables (for example \"ds\"); otherwise Tailwind theme variables such as --color-x would reference themselves.",
            remediation: "Set the 'prefix' setting to the prefix used by your token stylesheet.");
        return false;
    }

    /// <summary>
    /// Tokens matched by <paramref name="mapping"/> with the utility-name suffix each one gets; reports type mismatches and
    /// collisions within this mapping. Callers detect collisions between mappings.
    /// </summary>
    internal static IEnumerable<(ResolvedToken Token, string Suffix)> Match(ResolvedTokenSet tokens, TailwindMapping mapping, DiagnosticBag bag)
    {
        var types = Namespaces[mapping.Namespace];
        var names = new Dictionary<string, TokenPath>(StringComparer.Ordinal);
        var strip = TokenPath.Parse(mapping.StripPrefix ?? LiteralPrefix(mapping.Tokens));
        var matched = tokens.Tokens.Where(t => TokenPathPattern.IsMatch(mapping.Tokens, t.Path.ToString())).ToList();
        if (matched.Count == 0)
        {
            bag.Report(DiagnosticCatalog.InvalidMapping, $"Tailwind mapping '{mapping.Tokens}' → {mapping.Namespace} matches no tokens.", actual: mapping.Tokens);
        }
        foreach (var token in matched)
        {
            if (!token.IsValid) continue;
            if (!types.Contains(token.Type!))
            {
                bag.Report(DiagnosticCatalog.MappingTypeMismatch,
                    $"Tailwind namespace '{mapping.Namespace}' accepts {string.Join(" or ", types)}, but '{token.Path}' is a {token.Type}.",
                    token.Path.ToString(), token.Location, expected: string.Join(" | ", types), actual: token.Type);
                continue;
            }
            var relative = token.Path.StartsWith(strip) && token.Path.Length > strip.Length ? token.Path.RelativeTo(strip) : token.Path;
            var suffix = string.Join("-", relative.Segments.Where(static s => s != TokenPath.RootTokenName).Select(CssExporter.ToKebab));
            if (names.TryGetValue(suffix, out var existing))
            {
                bag.Report(DiagnosticCatalog.NameCollision, $"'{token.Path}' and '{existing}' both map to Tailwind name '{mapping.Namespace}{(suffix.Length == 0 ? "" : "-" + suffix)}'.",
                    token.Path.ToString(), token.Location);
                continue;
            }
            names[suffix] = token.Path;
            yield return (token, suffix);
        }
    }

    private static void AddLine(List<string> lines, string line)
    {
        if (!lines.Contains(line)) lines.Add(line);
    }

    private static IEnumerable<string> Declarations(string name, ResolvedToken token, string ns, TailwindExportOptions options, DiagnosticBag bag)
    {
        var reference = options.Values == IntegrationValueMode.Reference;
        if (token.Value is TypographyValue typography && ns == "text")
        {
            // Tailwind v4 font-size utilities read companion variables: --text-x--line-height, --text-x--letter-spacing, --text-x--font-weight.
            var baseName = CssExporter.GetVariableName(token.Path, options.Css);
            yield return $"{name}: {(reference ? $"var({baseName}-font-size)" : CssExporter.FormatValue(typography.FontSize, options.Css))};";
            yield return $"{name}--line-height: {(reference ? $"var({baseName}-line-height)" : CssExporter.FormatNumber(typography.LineHeight))};";
            yield return $"{name}--letter-spacing: {(reference ? $"var({baseName}-letter-spacing)" : CssExporter.FormatValue(typography.LetterSpacing, options.Css))};";
            yield return $"{name}--font-weight: {(reference ? $"var({baseName}-font-weight)" : CssExporter.FormatNumber(typography.FontWeight.Weight))};";
            yield break;
        }
        if (ns == "drop-shadow" && token.Value is ShadowValue shadow)
        {
            // drop-shadow() has no spread and no inset; refuse instead of silently dropping them.
            if (shadow.Layers.Any(static l => l.Inset || l.Spread.Value != 0))
            {
                bag.Report(DiagnosticCatalog.UnsupportedConversion, $"'{token.Path}' uses inset or spread, which drop-shadow() cannot express.", token.Path.ToString(), token.Location);
                yield break;
            }
            var layers = shadow.Layers.Select(l => $"{CssExporter.FormatValue(l.OffsetX, options.Css)} {CssExporter.FormatValue(l.OffsetY, options.Css)} {CssExporter.FormatValue(l.Blur, options.Css)} {CssExporter.FormatValue(l.Color, options.Css)}");
            yield return $"{name}: {string.Join(", ", layers)};";
            yield break;
        }
        var mode = LiteralOnlyNamespaces.Contains(ns) ? IntegrationValueMode.Literal : options.Values;
        var value = IntegrationSupport.Format(token, mode, options.Css, bag);
        if (value is not null) yield return $"{name}: {value};";
    }

    internal static string LiteralPrefix(string pattern)
    {
        var segments = pattern.Split('.').TakeWhile(static s => !s.Contains('*'));
        return string.Join('.', segments);
    }
}

/// <summary><see cref="ITokenExporter"/> adapter. Configuration: <c>{ "mappings": [ { "tokens": "color.**", "namespace": "color" } ] }</c>.</summary>
public sealed class TailwindTokenExporter : ITokenExporter
{
    public string Format => "tailwind";
    public string Description => "Tailwind CSS v4 @theme block with type-checked namespace mappings.";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["prefix"] = "Prefix of the runtime token variables referenced in reference mode.",
        ["values"] = "'reference' (default, @theme inline with var()) or 'literal'.",
        ["reset"] = "Reset Tailwind defaults for mapped namespaces. Default false.",
        ["colorFormat"] = "Color format for literal mode: 'auto', 'hex' or 'native'.",
    };

    /// <summary>Reads <c>{ "mappings": [ { "tokens": "color.**", "namespace": "color", "stripPrefix": "color" } ] }</c>.</summary>
    public static List<TailwindMapping> ReadMappings(TokenExportContext context, DiagnosticBag bag)
    {
        var mappings = new List<TailwindMapping>();
        if (context.Configuration?["mappings"] is SourceArray arr)
        {
            foreach (var item in arr.Items)
            {
                if (item is SourceObject o && o["tokens"] is SourceString t && o["namespace"] is SourceString ns)
                    mappings.Add(new TailwindMapping(t.Value, ns.Value, (o["stripPrefix"] as SourceString)?.Value));
                else bag.Report(DiagnosticCatalog.InvalidConfiguration, "Tailwind mappings need \"tokens\" and \"namespace\".", location: item.Location);
            }
        }
        else bag.Report(DiagnosticCatalog.InvalidConfiguration, "The Tailwind exporters need a \"mappings\" array in their configuration.");
        return mappings;
    }

    public TokenExportOutput Export(TokenExportContext context)
    {
        var bag = new DiagnosticBag();
        var mappings = ReadMappings(context, bag);
        var output = TailwindThemeExporter.Export(context.Tokens, new TailwindExportOptions
        {
            Mappings = mappings,
            Values = context.GetEnum("values", IntegrationValueMode.Reference),
            ResetMappedNamespaces = context.GetBool("reset", false),
            Css = CssExportOptions.Default with { Prefix = context.Get("prefix") ?? "", ColorFormat = context.GetEnum("colorFormat", CssColorFormat.Auto) },
        });
        return new TokenExportOutput(output.Content, [.. bag.ToSortedList(), .. output.Diagnostics]);
    }
}
