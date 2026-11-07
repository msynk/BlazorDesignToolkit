using System.Collections.Immutable;
using System.Text;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Theming;

public sealed record ThemeCssOptions
{
    public static ThemeCssOptions Default { get; } = new();

    /// <summary>Naming, color format and reference options. References are preserved by default so runtime overrides cascade.</summary>
    public CssExportOptions Css { get; init; } = CssExportOptions.Default with { References = CssReferenceMode.Preserve, IncludeHeader = false };
    /// <summary>Attribute holding the theme id. Default <c>data-theme</c>.</summary>
    public string ThemeAttribute { get; init; } = "data-theme";
    /// <summary>Prefix of axis attributes; the attribute for axis "mode" is <c>data-mode</c> by default.</summary>
    public string AxisAttributePrefix { get; init; } = "data-";
    /// <summary>Also apply the default theme to the scope root when no theme attribute is present.</summary>
    public bool DefaultThemeOnScopeRoot { get; init; } = true;
    /// <summary>Scope root for the default theme. Default <c>:root</c>.</summary>
    public string ScopeRoot { get; init; } = ":root";
    /// <summary>Emit <c>@media</c> variants so that the value <c>system</c> works without JavaScript.</summary>
    public bool IncludeSystemMediaQueries { get; init; } = true;
    /// <summary>
    /// Wrap the theme scope in <c>:is()</c> instead of <c>:where()</c>, adding the specificity of <c>:root:not([data-theme])</c>
    /// (0,2,0) to every rule. Needed when the output must override framework rules on <c>:root</c>, such as Bootstrap's.
    /// Relative ordering between theme rules is unchanged.
    /// </summary>
    public bool ElevateSpecificity { get; init; }
    /// <summary>Emit the CSS <c>color-scheme</c> property for the "mode" axis (light/dark) so native controls follow the theme.</summary>
    public bool EmitColorScheme { get; init; } = true;
    public string? Layer { get; init; }
    public bool IncludeHeader { get; init; } = true;
    /// <summary>Restrict output to these theme ids (default: all).</summary>
    public IReadOnlyList<string>? Themes { get; init; }
    /// <summary>
    /// Produces the declarations for one composed theme selection (default: the tokens as CSS custom properties, see
    /// <see cref="Css"/>). Integrations use it to emit framework variables per theme and mode with the same cascade-diff
    /// selectors, for example Bootstrap runtime variables whose values must be computed per theme.
    /// </summary>
    public Func<ThemeSelection, ResolvedTokenSet, DiagnosticBag, IReadOnlyList<CssDeclaration>>? Declarations { get; init; }

    public string AxisAttribute(string axis) => AxisAttributePrefix + axis;
}

/// <summary>One generated rule: the axis assignment it represents and its declarations.</summary>
public sealed record ThemeCssBlock(string ThemeId, ImmutableSortedDictionary<string, string> Assignment, IReadOnlyList<CssDeclaration> Declarations, string Selector);

public sealed class ThemeStylesheetResult(string css, IReadOnlyList<ThemeCssBlock> blocks, IReadOnlyList<Diagnostic> diagnostics)
{
    public string Css { get; } = css;
    public IReadOnlyList<ThemeCssBlock> Blocks { get; } = blocks;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Generates one stylesheet containing every theme and every axis combination. Switching themes or modes at runtime only
/// changes attributes on the scope root; no CSS is regenerated. Each variant rule contains only the declarations whose
/// values differ from what the cascade of less specific rules already produces, so the output stays small and
/// every combination resolves exactly to the composed token values. Output is deterministic.
/// </summary>
public static class ThemeStylesheetGenerator
{
    private const string ColorSchemeProperty = "color-scheme";

    public static ThemeStylesheetResult Generate(ThemeCatalog catalog, ThemeCssOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ThemeCssOptions.Default;
        var bag = new DiagnosticBag();
        bag.AddRange(catalog.Diagnostics);
        if (!IsAttributeName(options.ThemeAttribute) || !IsAttributeName(options.AxisAttributePrefix + "x") || !IsSimpleSelector(options.ScopeRoot))
        {
            bag.Report(DiagnosticCatalog.InvalidIdentifier, $"Attribute names '{options.ThemeAttribute}' / '{options.AxisAttributePrefix}' are not valid HTML attribute names.");
            return new ThemeStylesheetResult("", [], bag.ToSortedList());
        }
        var sb = new StringBuilder();
        if (options.IncludeHeader) sb.Append(CssExporter.Banner).Append('\n');
        var indent = "";
        if (options.Layer is not null)
        {
            sb.Append("@layer ").Append(options.Layer).Append(" {\n");
            indent = "  ";
        }
        var blocks = new List<ThemeCssBlock>();
        var themes = catalog.Themes.Where(t => options.Themes is null || options.Themes.Contains(t.Id)).ToList();
        var seenDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var theme in themes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.Append(indent).Append("/* Theme: ").Append(CssExporter.SanitizeComment(theme.DisplayName ?? theme.Id)).Append(" (").Append(theme.Id).Append(") */\n");
            GenerateTheme(catalog, theme, options, sb, indent, blocks, bag, seenDiagnostics, cancellationToken);
        }
        if (options.Layer is not null) sb.Append("}\n");
        return new ThemeStylesheetResult(sb.ToString(), blocks, bag.ToSortedList());
    }

    private static bool IsAttributeName(string name)
        => name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsSimpleSelector(string selector)
        => selector.Length > 0 && selector.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.' or '#');

    private static void GenerateTheme(ThemeCatalog catalog, ThemeDefinition theme, ThemeCssOptions options, StringBuilder sb, string indent,
        List<ThemeCssBlock> blocks, DiagnosticBag bag, HashSet<string> seenDiagnostics, CancellationToken cancellationToken)
    {
        var axes = catalog.Axes;
        var emitted = new List<(ImmutableSortedDictionary<string, string> Assignment, Dictionary<string, string> Values)>();
        var empty = ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);

        // Enumerate assignments by increasing size (specificity), then axis order, then value order.
        var assignments = new List<ImmutableSortedDictionary<string, string>> { empty };
        for (var size = 1; size <= axes.Length; size++)
        {
            foreach (var subset in Combinations(axes.Length, size))
            {
                IEnumerable<ImmutableSortedDictionary<string, string>> combos = [empty];
                foreach (var index in subset)
                {
                    var axis = axes[index];
                    combos = combos.SelectMany(c => axis.Values.Where(v => v != axis.Default).Select(v => c.Add(axis.Name, v))).ToList();
                }
                assignments.AddRange(combos);
            }
        }

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = new ThemeSelection(theme.Id, axes.ToDictionary(a => a.Name, a => assignment.GetValueOrDefault(a.Name) ?? a.Default));
            var resolution = catalog.Resolve(selection, cancellationToken: cancellationToken);
            foreach (var d in resolution.Diagnostics)
            {
                if (seenDiagnostics.Add(d.RuleId + "|" + d.TokenPath + "|" + d.Message + "|" + d.Scope)) bag.Add(d);
            }
            var exportBag = new DiagnosticBag();
            var declarations = options.Declarations is { } custom
                ? custom(selection, resolution.Tokens, exportBag).ToList()
                : CssExporter.GetDeclarations(resolution.Tokens, options.Css, exportBag).ToList();
            foreach (var d in exportBag.Items)
            {
                var scoped = d.WithScope(ThemeComposer.Scope(selection));
                if (seenDiagnostics.Add(d.RuleId + "|" + d.TokenPath + "|" + d.Message)) bag.Add(scoped);
            }
            if (options.EmitColorScheme && selection[ThemeAxes.Mode] is "light" or "dark")
            {
                declarations.Insert(0, new CssDeclaration(ColorSchemeProperty, selection[ThemeAxes.Mode]!, TokenPath.Empty));
            }

            var predicted = Predict(emitted, assignment);
            var changed = declarations.Where(d => !predicted.TryGetValue(d.Name, out var p) || p != d.Value).ToList();
            // Only what a rule actually declares takes part in the cascade of more specific rules.
            emitted.Add((assignment, changed.ToDictionary(static d => d.Name, static d => d.Value, StringComparer.Ordinal)));
            if (changed.Count == 0 && assignment.Count > 0) continue;

            var selector = Selector(catalog, theme, assignment, options, systemAxes: []);
            blocks.Add(new ThemeCssBlock(theme.Id, assignment, changed, selector));
            CssExporter.WriteBlock(sb, selector, changed, null, options.Css.IncludeComments, indent);

            if (!options.IncludeSystemMediaQueries) continue;
            var systemCapable = assignment.Where(a => catalog.FindAxis(a.Key)!.SystemMediaQueries.ContainsKey(a.Value)).Select(static a => a.Key).ToList();
            for (var size = 1; size <= systemCapable.Count; size++)
            {
                foreach (var subset in Combinations(systemCapable.Count, size))
                {
                    var systemAxes = subset.Select(i => systemCapable[i]).ToHashSet(StringComparer.Ordinal);
                    var queries = systemAxes.Order(StringComparer.Ordinal).Select(a => catalog.FindAxis(a)!.SystemMediaQueries[assignment[a]]);
                    sb.Append(indent).Append("@media ").Append(string.Join(" and ", queries)).Append(" {\n");
                    CssExporter.WriteBlock(sb, Selector(catalog, theme, assignment, options, systemAxes), changed, null, options.Css.IncludeComments, indent + "  ");
                    sb.Append(indent).Append("}\n");
                }
            }
        }
    }

    /// <summary>Value the cascade produces for each property from already emitted (less or equally specific, earlier) rules.</summary>
    private static Dictionary<string, string> Predict(List<(ImmutableSortedDictionary<string, string> Assignment, Dictionary<string, string> Values)> emitted,
        ImmutableSortedDictionary<string, string> target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var rank = new Dictionary<string, (int Size, int Order)>(StringComparer.Ordinal);
        for (var i = 0; i < emitted.Count; i++)
        {
            var (assignment, values) = emitted[i];
            if (!assignment.All(a => target.TryGetValue(a.Key, out var v) && v == a.Value)) continue;
            foreach (var (name, value) in values)
            {
                if (rank.TryGetValue(name, out var r) && (r.Size > assignment.Count || (r.Size == assignment.Count && r.Order > i))) continue;
                rank[name] = (assignment.Count, i);
                result[name] = value;
            }
        }
        return result;
    }

    private static string Selector(ThemeCatalog catalog, ThemeDefinition theme, ImmutableSortedDictionary<string, string> assignment, ThemeCssOptions options, HashSet<string> systemAxes)
    {
        var themeSelector = $"[{options.ThemeAttribute}=\"{theme.Id}\"]";
        var wrapper = options.ElevateSpecificity ? ":is" : ":where";
        var scope = theme.Id == catalog.DefaultThemeId && options.DefaultThemeOnScopeRoot
            ? $"{wrapper}({options.ScopeRoot}:not([{options.ThemeAttribute}]), {themeSelector})"
            : options.ElevateSpecificity
                ? $":is({options.ScopeRoot}:not([{options.ThemeAttribute}]):not(*), {themeSelector})" // same (0,2,0) specificity for every theme
                : $":where({themeSelector})";
        var sb = new StringBuilder(scope);
        // Attribute order follows axis declaration order so selectors are stable.
        foreach (var axis in catalog.Axes)
        {
            if (!assignment.TryGetValue(axis.Name, out var value)) continue;
            sb.Append('[').Append(options.AxisAttribute(axis.Name)).Append("=\"").Append(systemAxes.Contains(axis.Name) ? ThemeAxes.System : value).Append("\"]");
        }
        return sb.ToString();
    }

    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var indices = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return [.. indices];
            var i = k - 1;
            while (i >= 0 && indices[i] == n - k + i) i--;
            if (i < 0) yield break;
            indices[i]++;
            for (var j = i + 1; j < k; j++) indices[j] = indices[j - 1] + 1;
        }
    }

    /// <summary>
    /// Custom-property declarations that runtime overrides change for <paramref name="selection"/>; apply them inline on the
    /// scope root. Only differing declarations are returned so the DOM update is minimal.
    /// </summary>
    public static IReadOnlyList<CssDeclaration> GetOverrideDeclarations(ThemeCatalog catalog, ThemeSelection selection, SourceObject overrides,
        ThemeCssOptions? options, out IReadOnlyList<Diagnostic> diagnostics)
    {
        options ??= ThemeCssOptions.Default;
        var concrete = catalog.ResolveSystemValues(catalog.Normalize(selection, out _), null);
        var baseline = catalog.Resolve(concrete);
        var withOverrides = catalog.Resolve(concrete, overrides);
        var bag = new DiagnosticBag();
        var before = CssExporter.GetDeclarations(baseline.Tokens, options.Css, new DiagnosticBag()).ToDictionary(static d => d.Name, static d => d.Value, StringComparer.Ordinal);
        var after = CssExporter.GetDeclarations(withOverrides.Tokens, options.Css, bag);
        bag.AddRange(withOverrides.Diagnostics.Where(d => !baseline.Diagnostics.Any(b => b.RuleId == d.RuleId && b.TokenPath == d.TokenPath && b.Message == d.Message)));
        diagnostics = bag.ToSortedList();
        return after.Where(d => !before.TryGetValue(d.Name, out var v) || v != d.Value).ToList();
    }
}
