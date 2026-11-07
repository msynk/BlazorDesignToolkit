using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;

namespace BlazorDesignToolkit.Theming;

/// <summary>
/// Checks that switching an axis value updates every token a variant touches: a token defined by a layer for
/// <c>axis=v</c> must also be defined for every other value of that axis, either by a layer for that value or by an
/// unconditional base layer. Otherwise the token keeps a stale value (or disappears) after switching (BDT6005).
/// Also lists, per theme, which tokens each variant overrides, for reporting.
/// </summary>
public static class ThemeCompletenessAnalyzer
{
    public sealed record VariantSummary(string ThemeId, string Axis, string Value, IReadOnlyList<string> OverriddenTokens);

    public static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<VariantSummary> Variants) Analyze(ThemeCatalog catalog)
    {
        var bag = new DiagnosticBag();
        var variants = new List<VariantSummary>();
        foreach (var theme in catalog.Themes)
        {
            var layers = catalog.GetInheritanceChain(theme.Id).SelectMany(static t => t.Layers).ToList();
            var basePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in layers.Where(static l => l.When.IsEmpty))
            {
                foreach (var (path, _) in ThemeComposer.EnumerateTypedTokens(layer.Tokens, TokenPath.Empty, null)) basePaths.Add(path.ToString());
            }
            foreach (var axis in catalog.Axes)
            {
                var perValue = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                foreach (var value in axis.Values)
                {
                    var paths = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var layer in layers.Where(l => l.When.TryGetValue(axis.Name, out var v) && v == value))
                    {
                        foreach (var (path, _) in ThemeComposer.EnumerateTypedTokens(layer.Tokens, TokenPath.Empty, null)) paths.Add(path.ToString());
                    }
                    perValue[value] = paths;
                    if (paths.Count > 0) variants.Add(new VariantSummary(theme.Id, axis.Name, value, [.. paths]));
                }
                var union = perValue.Values.SelectMany(static p => p).ToHashSet(StringComparer.Ordinal);
                foreach (var (value, paths) in perValue)
                {
                    foreach (var missing in union.Where(p => !paths.Contains(p) && !basePaths.Contains(p)).Order(StringComparer.Ordinal))
                    {
                        var definedIn = perValue.Where(kv => kv.Value.Contains(missing)).Select(static kv => kv.Key);
                        bag.Report(DiagnosticCatalog.IncompleteThemeContext,
                            $"Theme '{theme.Id}': '{missing}' is defined for {axis.Name}={string.Join("/", definedIn)} but not for {axis.Name}={value}.",
                            missing, remediation: $"Define '{missing}' in the {axis.Name}={value} layer so switching variants updates it.");
                    }
                }
            }
        }
        return (bag.ToSortedList(), variants);
    }
}
