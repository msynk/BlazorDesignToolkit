using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Theming;

/// <summary>Well-known theme axes. Theme identity is separate from every axis.</summary>
public static class ThemeAxes
{
    /// <summary>Color mode: <c>light</c> | <c>dark</c>.</summary>
    public const string Mode = "mode";
    /// <summary>Contrast preference: <c>standard</c> | <c>more</c>.</summary>
    public const string Contrast = "contrast";
    /// <summary>Density profile, e.g. <c>comfortable</c> | <c>compact</c>.</summary>
    public const string Density = "density";
    /// <summary>Text direction: <c>ltr</c> | <c>rtl</c>.</summary>
    public const string Direction = "dir";

    /// <summary>Reserved axis value meaning "follow the operating-system preference".</summary>
    public const string System = "system";

    public static ThemeAxisDefinition ColorMode(string defaultValue = "light") => new(Mode, ["light", "dark"], defaultValue)
    {
        DisplayName = "Color mode",
        SystemMediaQueries = defaultValue == "dark"
            ? ImmutableDictionary<string, string>.Empty.Add("light", "(prefers-color-scheme: light)")
            : ImmutableDictionary<string, string>.Empty.Add("dark", "(prefers-color-scheme: dark)"),
    };

    public static ThemeAxisDefinition ContrastPreference() => new(Contrast, ["standard", "more"], "standard")
    {
        DisplayName = "Contrast",
        SystemMediaQueries = ImmutableDictionary<string, string>.Empty.Add("more", "(prefers-contrast: more)"),
    };

    public static ThemeAxisDefinition DensityProfile(params string[] values) => new(Density, values.Length == 0 ? ["comfortable", "compact"] : values, values.Length == 0 ? "comfortable" : values[0])
    {
        DisplayName = "Density",
    };

    public static ThemeAxisDefinition TextDirection() => new(Direction, ["ltr", "rtl"], "ltr") { DisplayName = "Direction" };
}

/// <summary>
/// An independent dimension of a theme selection (mode, contrast, density, direction or a custom axis).
/// Values are validated identifiers so they can be used safely as HTML attribute values and CSS selectors.
/// </summary>
public sealed partial record ThemeAxisDefinition
{
    public ThemeAxisDefinition(string name, IReadOnlyList<string> values, string defaultValue)
    {
        if (!ThemeIdentifiers.IsValid(name)) throw new ArgumentException($"Invalid axis name '{name}'. {ThemeIdentifiers.Rule}", nameof(name));
        if (values.Count == 0) throw new ArgumentException("An axis needs at least one value.", nameof(values));
        foreach (var v in values)
        {
            if (!ThemeIdentifiers.IsValid(v)) throw new ArgumentException($"Invalid value '{v}' for axis '{name}'. {ThemeIdentifiers.Rule}", nameof(values));
            if (v == ThemeAxes.System) throw new ArgumentException($"'{ThemeAxes.System}' is reserved; configure SystemMediaQueries instead.", nameof(values));
        }
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) throw new ArgumentException($"Axis '{name}' has duplicate values.", nameof(values));
        if (!values.Contains(defaultValue)) throw new ArgumentException($"Default '{defaultValue}' is not a value of axis '{name}'.", nameof(defaultValue));
        Name = name;
        Values = [.. values];
        Default = defaultValue;
    }

    public string Name { get; }
    public ImmutableArray<string> Values { get; }
    public string Default { get; }
    public string? DisplayName { get; init; }

    /// <summary>
    /// Media queries that select a value when the axis is set to <c>system</c>, e.g. dark → <c>(prefers-color-scheme: dark)</c>.
    /// When non-empty, the axis accepts the value <c>system</c>. When no query matches (or no browser is available, e.g. during
    /// prerendering) the <see cref="Default"/> value applies, so the default value itself must not have a query.
    /// </summary>
    public ImmutableDictionary<string, string> SystemMediaQueries
    {
        get => _systemMediaQueries;
        init
        {
            foreach (var (key, query) in value)
            {
                if (!Values.Contains(key)) throw new ArgumentException($"SystemMediaQueries of axis '{Name}' references unknown value '{key}'.", nameof(value));
                if (key == Default) throw new ArgumentException($"The default value '{Default}' of axis '{Name}' must not have a system media query; it is the fallback.", nameof(value));
                if (!MediaQueryPattern().IsMatch(query)) throw new ArgumentException($"Unsupported media query '{query}'. Use a single feature such as '(prefers-color-scheme: dark)'.", nameof(value));
            }
            _systemMediaQueries = value;
        }
    }

    private readonly ImmutableDictionary<string, string> _systemMediaQueries = ImmutableDictionary<string, string>.Empty;

    [GeneratedRegex(@"^\([a-z-]+\s*:\s*[a-z-]+\)$")]
    private static partial Regex MediaQueryPattern();

    public bool SupportsSystem => !SystemMediaQueries.IsEmpty;

    public bool Accepts(string value) => Values.Contains(value) || (value == ThemeAxes.System && SupportsSystem);
}

public static partial class ThemeIdentifiers
{
    public const string Rule = "Identifiers must start with a letter and contain only ASCII letters, digits, '-' and '_'.";

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex Pattern();
}

/// <summary>A token layer of a theme, applied when every condition in <see cref="When"/> matches the selection.</summary>
public sealed record ThemeLayer
{
    public ThemeLayer(SourceObject tokens, string? name = null, IReadOnlyDictionary<string, string>? when = null)
    {
        Tokens = tokens;
        Name = name;
        When = when is null ? ImmutableDictionary<string, string>.Empty : when.ToImmutableDictionary(StringComparer.Ordinal);
    }

    /// <summary>DTCG token tree contributed by this layer.</summary>
    public SourceObject Tokens { get; }
    /// <summary>Human-readable origin (usually a file name) used in diagnostics.</summary>
    public string? Name { get; }
    /// <summary>Axis conditions; an empty map means the layer always applies.</summary>
    public ImmutableDictionary<string, string> When { get; }
    /// <summary>
    /// When false (default for conditional layers), tokens that do not exist in lower layers are reported (BDT6001) because
    /// they usually indicate a typo or an incomplete base.
    /// </summary>
    public bool? MayAddTokens { get; init; }

    public bool Matches(IReadOnlyDictionary<string, string> axes)
        => When.All(c => axes.TryGetValue(c.Key, out var v) && string.Equals(v, c.Value, StringComparison.Ordinal));
}

/// <summary>A named theme (brand). Layers apply in order after the layers of the theme it extends.</summary>
public sealed record ThemeDefinition
{
    public ThemeDefinition(string id, IReadOnlyList<ThemeLayer> layers)
    {
        if (!ThemeIdentifiers.IsValid(id)) throw new ArgumentException($"Invalid theme id '{id}'. {ThemeIdentifiers.Rule}", nameof(id));
        Id = id;
        Layers = [.. layers];
    }

    public string Id { get; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    /// <summary>Id of a theme whose layers are applied first (inheritance).</summary>
    public string? Extends { get; init; }
    public ImmutableArray<ThemeLayer> Layers { get; init; }
    /// <summary>Arbitrary user-defined metadata (brand owner, version, documentation links…).</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>The theme and axis values to resolve. Axis values may be <c>system</c> for axes that support it.</summary>
public sealed record ThemeSelection
{
    public ThemeSelection(string themeId, IReadOnlyDictionary<string, string>? axes = null)
    {
        ThemeId = themeId;
        Axes = axes is null ? ImmutableSortedDictionary<string, string>.Empty : axes.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    public string ThemeId { get; }
    public ImmutableSortedDictionary<string, string> Axes { get; }

    public string? this[string axis] => Axes.GetValueOrDefault(axis);

    public ThemeSelection WithTheme(string themeId) => new(themeId, Axes);

    public ThemeSelection WithAxis(string axis, string value) => new(ThemeId, Axes.SetItem(axis, value));

    /// <summary>Stable key, e.g. <c>aurora|density=compact|mode=dark</c>.</summary>
    public string Key => ThemeId + string.Concat(Axes.Select(static a => $"|{a.Key}={a.Value}"));

    public override string ToString() => Key;

    public bool Equals(ThemeSelection? other) => other is not null && Key == other.Key;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}

/// <summary>Operating-system preferences reported by a browser adapter.</summary>
public sealed record SystemPreferences
{
    public static SystemPreferences Unknown { get; } = new();

    /// <summary>"light", "dark" or null when unknown.</summary>
    public string? ColorScheme { get; init; }
    /// <summary>"more", "less", "custom", "no-preference" or null.</summary>
    public string? Contrast { get; init; }
    public bool ForcedColors { get; init; }
    public bool ReducedMotion { get; init; }

    /// <summary>Evaluates a media query of the kind used in <see cref="ThemeAxisDefinition.SystemMediaQueries"/>.</summary>
    public bool? Matches(string mediaQuery)
    {
        var q = mediaQuery.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        return q switch
        {
            "(prefers-color-scheme:dark)" => ColorScheme is null ? null : ColorScheme == "dark",
            "(prefers-color-scheme:light)" => ColorScheme is null ? null : ColorScheme == "light",
            "(prefers-contrast:more)" => Contrast is null ? null : Contrast == "more" || ForcedColors,
            "(prefers-contrast:less)" => Contrast is null ? null : Contrast == "less",
            "(forced-colors:active)" => ForcedColors,
            "(prefers-reduced-motion:reduce)" => ReducedMotion,
            _ => null,
        };
    }
}
