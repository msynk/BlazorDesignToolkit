using System.Text.RegularExpressions;

namespace BlazorDesignToolkit.Theming.Tests;

/// <summary>
/// A tiny evaluator for the stylesheets produced by <see cref="ThemeStylesheetGenerator"/>: it applies selector matching,
/// specificity, source order and the supported media features to compute custom-property values for an element.
/// Used to prove that every theme/axis combination resolves to exactly the composed token values.
/// </summary>
internal sealed partial class CssCascade
{
    private readonly List<Rule> _rules = [];

    private sealed record Rule(IReadOnlyList<SelectorAlternative> Alternatives, int Specificity, IReadOnlyList<string> Media, Dictionary<string, string> Declarations, int Order);

    private sealed record SelectorAlternative(bool RequiresRootWithoutTheme, string? Theme, IReadOnlyList<(string Name, string Value)> Attributes);

    public CssCascade(string css)
    {
        var media = new Stack<string>();
        Rule? current = null;
        var order = 0;
        foreach (var raw in css.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("/*", StringComparison.Ordinal)) continue;
            if (line.StartsWith("@layer", StringComparison.Ordinal)) { media.Push(""); continue; }
            if (line.StartsWith("@media", StringComparison.Ordinal)) { media.Push(line[6..^1].Trim()); continue; }
            if (line.EndsWith('{'))
            {
                current = ParseRule(line[..^1].Trim(), media.Where(static m => m.Length > 0).SelectMany(static m => m.Split(" and ", StringSplitOptions.TrimEntries)).ToList(), order++);
                _rules.Add(current);
                continue;
            }
            if (line == "}")
            {
                if (current is not null) current = null;
                else if (media.Count > 0) media.Pop();
                continue;
            }
            var colon = line.IndexOf(':');
            current!.Declarations[line[..colon]] = line[(colon + 1)..].TrimEnd(';').Trim();
        }
    }

    private static Rule ParseRule(string selector, List<string> media, int order)
    {
        var m = SelectorPattern().Match(selector);
        if (!m.Success) throw new FormatException($"Unexpected selector: {selector}");
        var alternatives = new List<SelectorAlternative>();
        var attributes = AttributePattern().Matches(m.Groups["tail"].Value).Select(a => (a.Groups[1].Value, a.Groups[2].Value)).ToList();
        foreach (var alt in m.Groups["where"].Value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (alt.StartsWith(":root:not(", StringComparison.Ordinal)) alternatives.Add(new SelectorAlternative(true, null, attributes));
            else alternatives.Add(new SelectorAlternative(false, AttributePattern().Match(alt).Groups[2].Value, attributes));
        }
        return new Rule(alternatives, attributes.Count, media, [], order);
    }

    /// <summary>Computes declarations for the root element with the given attributes and media features.</summary>
    public Dictionary<string, string> Evaluate(IReadOnlyDictionary<string, string> attributes, ISet<string>? mediaFeatures = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var matching = _rules.Where(r => r.Media.All(q => mediaFeatures?.Contains(q) == true) && r.Alternatives.Any(a => Matches(a, attributes)))
            .OrderBy(static r => r.Specificity).ThenBy(static r => r.Order);
        foreach (var rule in matching)
        {
            foreach (var (k, v) in rule.Declarations) result[k] = v;
        }
        return result;
    }

    private static bool Matches(SelectorAlternative alt, IReadOnlyDictionary<string, string> attributes)
    {
        if (alt.RequiresRootWithoutTheme && attributes.ContainsKey("data-theme")) return false;
        if (alt.Theme is not null && (!attributes.TryGetValue("data-theme", out var t) || t != alt.Theme)) return false;
        return alt.Attributes.All(a => attributes.TryGetValue(a.Name, out var v) && v == a.Value);
    }

    [GeneratedRegex(@"^:where\((?<where>.*?)\)(?<tail>(\[[^\]]+\])*)$")]
    private static partial Regex SelectorPattern();

    [GeneratedRegex(@"\[([a-z-]+)=""([^""]*)""\]")]
    private static partial Regex AttributePattern();
}
