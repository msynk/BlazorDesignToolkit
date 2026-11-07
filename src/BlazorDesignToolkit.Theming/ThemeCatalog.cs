using System.Collections.Concurrent;
using System.Collections.Immutable;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Theming;

/// <summary>
/// An immutable set of axes and themes. Build it with <see cref="ThemeCatalogBuilder"/>; the catalog validates ids,
/// inheritance (missing or circular <c>Extends</c>) and layer conditions up front.
/// </summary>
public sealed class ThemeCatalog
{
    private readonly ConcurrentDictionary<string, ThemeResolution> _cache = new(StringComparer.Ordinal);

    internal ThemeCatalog(ImmutableArray<ThemeAxisDefinition> axes, ImmutableArray<ThemeDefinition> themes, string defaultThemeId,
        IReadOnlyList<Diagnostic> diagnostics, TokenResolutionOptions resolution)
    {
        Axes = axes;
        Themes = themes;
        DefaultThemeId = defaultThemeId;
        Diagnostics = diagnostics;
        ResolutionOptions = resolution;
    }

    public ImmutableArray<ThemeAxisDefinition> Axes { get; }
    public ImmutableArray<ThemeDefinition> Themes { get; }
    public string DefaultThemeId { get; }
    /// <summary>Problems found while building the catalog (BDT6003, BDT6004, BDT6009, BDT6010).</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public TokenResolutionOptions ResolutionOptions { get; }
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    public ThemeDefinition? FindTheme(string id) => Themes.FirstOrDefault(t => t.Id == id);

    public ThemeAxisDefinition? FindAxis(string name) => Axes.FirstOrDefault(a => a.Name == name);

    /// <summary>The selection with the default theme and every axis at its default value.</summary>
    public ThemeSelection DefaultSelection => new(DefaultThemeId, Axes.ToDictionary(static a => a.Name, static a => a.Default));

    /// <summary>
    /// Completes and validates a selection: unknown themes fall back to the default theme, missing or invalid axis values
    /// fall back to the axis default. Problems are returned as diagnostics so callers can surface them.
    /// </summary>
    public ThemeSelection Normalize(ThemeSelection selection, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var bag = new DiagnosticBag();
        var themeId = selection.ThemeId;
        if (FindTheme(themeId) is null)
        {
            bag.Report(DiagnosticCatalog.ThemeNotFound, $"Theme '{themeId}' is not registered; using '{DefaultThemeId}'.", actual: themeId,
                expected: string.Join(", ", Themes.Select(static t => t.Id)));
            themeId = DefaultThemeId;
        }
        var axes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var axis in Axes)
        {
            var value = selection[axis.Name];
            if (value is null) value = axis.Default;
            else if (!axis.Accepts(value))
            {
                bag.Report(DiagnosticCatalog.InvalidThemeSelection, $"'{value}' is not a value of axis '{axis.Name}'; using '{axis.Default}'.", actual: value,
                    expected: string.Join(", ", axis.Values.Concat(axis.SupportsSystem ? [ThemeAxes.System] : [])));
                value = axis.Default;
            }
            axes[axis.Name] = value;
        }
        foreach (var key in selection.Axes.Keys.Where(k => FindAxis(k) is null))
        {
            bag.Report(DiagnosticCatalog.InvalidThemeSelection, $"Unknown axis '{key}' is ignored.", actual: key);
        }
        diagnostics = bag.ToSortedList();
        return new ThemeSelection(themeId, axes);
    }

    /// <summary>Replaces <c>system</c> values using the reported preferences (or the axis fallback when unknown).</summary>
    public ThemeSelection ResolveSystemValues(ThemeSelection selection, SystemPreferences? preferences)
    {
        var result = selection;
        foreach (var axis in Axes)
        {
            if (selection[axis.Name] != ThemeAxes.System) continue;
            var value = axis.Default;
            foreach (var (candidate, query) in axis.SystemMediaQueries.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
            {
                if (preferences?.Matches(query) == true)
                {
                    value = candidate;
                    break;
                }
            }
            result = result.WithAxis(axis.Name, value);
        }
        return result;
    }

    /// <summary>Theme ids from the root ancestor to <paramref name="themeId"/>.</summary>
    public IReadOnlyList<ThemeDefinition> GetInheritanceChain(string themeId)
    {
        var chain = new List<ThemeDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = FindTheme(themeId);
        while (current is not null && seen.Add(current.Id))
        {
            chain.Insert(0, current);
            current = current.Extends is null ? null : FindTheme(current.Extends);
        }
        return chain;
    }

    /// <summary>
    /// Resolves the effective token set for a concrete selection (no <c>system</c> values). Results are cached per selection;
    /// runtime overrides are applied as a final layer and are not cached.
    /// </summary>
    public ThemeResolution Resolve(ThemeSelection selection, SourceObject? overrides = null, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(selection, out var selectionDiagnostics);
        if (normalized.Axes.Values.Contains(ThemeAxes.System)) normalized = ResolveSystemValues(normalized, null);
        if (overrides is null && _cache.TryGetValue(normalized.Key, out var cached)) return cached;
        var result = ThemeComposer.Compose(this, normalized, overrides, selectionDiagnostics, cancellationToken);
        if (overrides is null) _cache.TryAdd(normalized.Key, result);
        return result;
    }

    /// <summary>Every concrete combination of axis values (cartesian product), in axis/value declaration order.</summary>
    public IEnumerable<ImmutableSortedDictionary<string, string>> EnumerateAxisCombinations()
    {
        IEnumerable<ImmutableSortedDictionary<string, string>> result = [ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal)];
        foreach (var axis in Axes)
        {
            var current = axis;
            result = result.SelectMany(r => current.Values.Select(v => r.Add(current.Name, v))).ToList();
        }
        return result;
    }
}

/// <summary>The effective tokens of one theme selection.</summary>
public sealed class ThemeResolution(ThemeSelection selection, ResolvedTokenSet tokens, IReadOnlyList<Diagnostic> diagnostics, SourceObject mergedTokens)
{
    public ThemeSelection Selection { get; } = selection;
    public ResolvedTokenSet Tokens { get; } = tokens;
    /// <summary>Theme diagnostics plus token resolution diagnostics, sorted.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    /// <summary>The merged token tree before reference resolution.</summary>
    public SourceObject MergedTokens { get; } = mergedTokens;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Fluent builder for <see cref="ThemeCatalog"/>.</summary>
public sealed class ThemeCatalogBuilder
{
    private readonly List<ThemeAxisDefinition> _axes = [];
    private readonly List<ThemeDefinition> _themes = [];
    private string? _default;

    public TokenResolutionOptions ResolutionOptions { get; set; } = TokenResolutionOptions.Default;

    public ThemeCatalogBuilder AddAxis(ThemeAxisDefinition axis)
    {
        _axes.RemoveAll(a => a.Name == axis.Name);
        _axes.Add(axis);
        return this;
    }

    /// <summary>Adds the color-mode, contrast and density axes with their usual values.</summary>
    public ThemeCatalogBuilder AddStandardAxes(params string[] densities)
        => AddAxis(ThemeAxes.ColorMode()).AddAxis(ThemeAxes.ContrastPreference()).AddAxis(ThemeAxes.DensityProfile(densities));

    public ThemeCatalogBuilder AddTheme(ThemeDefinition theme, bool isDefault = false)
    {
        _themes.Add(theme);
        if (isDefault || _default is null) _default = isDefault ? theme.Id : _default ?? theme.Id;
        return this;
    }

    public ThemeCatalogBuilder SetDefaultTheme(string id)
    {
        _default = id;
        return this;
    }

    public ThemeCatalog Build()
    {
        var bag = new DiagnosticBag();
        var themes = new List<ThemeDefinition>();
        foreach (var theme in _themes)
        {
            if (themes.Any(t => t.Id == theme.Id))
            {
                bag.Report(DiagnosticCatalog.DuplicateTheme, $"Theme '{theme.Id}' is registered more than once; the first registration is used.", actual: theme.Id);
                continue;
            }
            themes.Add(theme);
        }
        foreach (var theme in themes)
        {
            if (theme.Extends is not null && themes.All(t => t.Id != theme.Extends))
            {
                bag.Report(DiagnosticCatalog.ThemeNotFound, $"Theme '{theme.Id}' extends unknown theme '{theme.Extends}'.", actual: theme.Extends);
            }
            var seen = new List<string> { theme.Id };
            var current = theme;
            while (current.Extends is { } parent && themes.FirstOrDefault(t => t.Id == parent) is { } next)
            {
                if (seen.Contains(next.Id))
                {
                    bag.Report(DiagnosticCatalog.CircularThemeInheritance, $"Theme inheritance is circular: {string.Join(" -> ", seen.Append(next.Id))}.",
                        referenceChain: [.. seen, next.Id]);
                    break;
                }
                seen.Add(next.Id);
                current = next;
            }
            foreach (var layer in theme.Layers)
            {
                foreach (var (axis, value) in layer.When)
                {
                    var definition = _axes.FirstOrDefault(a => a.Name == axis);
                    if (definition is null)
                        bag.Report(DiagnosticCatalog.InvalidThemeSelection, $"Layer '{layer.Name}' of theme '{theme.Id}' depends on unknown axis '{axis}'.", actual: axis);
                    else if (!definition.Values.Contains(value))
                        bag.Report(DiagnosticCatalog.InvalidThemeSelection, $"Layer '{layer.Name}' of theme '{theme.Id}' depends on unknown value '{value}' of axis '{axis}'.",
                            actual: value, expected: string.Join(", ", definition.Values));
                }
            }
        }
        if (themes.Count == 0) throw new InvalidOperationException("A theme catalog needs at least one theme.");
        var defaultId = _default is not null && themes.Any(t => t.Id == _default) ? _default : themes[0].Id;
        if (_default is not null && defaultId != _default)
        {
            bag.Report(DiagnosticCatalog.ThemeNotFound, $"Default theme '{_default}' is not registered; using '{defaultId}'.", actual: _default);
        }
        return new ThemeCatalog([.. _axes], [.. themes], defaultId, bag.ToSortedList(), ResolutionOptions);
    }
}
