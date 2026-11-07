using BlazorDesignToolkit.Tokens.Diagnostics;

namespace BlazorDesignToolkit.Tokens.Validation;

public enum NameCase
{
    /// <summary>lower-case words separated by '-' (digits allowed): <c>on-surface</c>, <c>100</c>.</summary>
    KebabCase,
    /// <summary><c>onSurface</c>.</summary>
    CamelCase,
    /// <summary><c>on_surface</c>.</summary>
    SnakeCase,
    /// <summary><c>OnSurface</c>.</summary>
    PascalCase,
}

/// <summary>A naming rule applied to every segment of token paths that match <see cref="Pattern"/>.</summary>
public sealed record NamingRule
{
    /// <summary>Token-path glob ('*' = one segment, '**' = any). Default: all tokens.</summary>
    public string Pattern { get; init; } = "**";
    public NameCase? Case { get; init; }
    /// <summary>Optional regular expression each segment must match (in addition to <see cref="Case"/>).</summary>
    public string? SegmentRegex { get; init; }
    /// <summary>Optional regular expression the full dot-separated path must match.</summary>
    public string? PathRegex { get; init; }
}

/// <summary>A token that must exist (for example a semantic token every theme must define).</summary>
public sealed record RequiredToken(string Path, string? Type = null);

/// <summary>Assigns tokens to layers by path pattern so that layering rules can be checked.</summary>
public sealed record TokenLayerConvention
{
    public IReadOnlyList<string> Primitive { get; init; } = [];
    public IReadOnlyList<string> Semantic { get; init; } = [];
    public IReadOnlyList<string> Component { get; init; } = [];
    /// <summary>Semantic tokens should alias other tokens rather than hold raw values. Default true.</summary>
    public bool SemanticMustAlias { get; init; } = true;
    /// <summary>Component tokens should reference semantic tokens, not primitives. Default true.</summary>
    public bool ComponentShouldUseSemantic { get; init; } = true;
}

public enum ContrastKind
{
    /// <summary>Normal-size text: 4.5:1 (AA), 7:1 (AAA). WCAG 2.2 SC 1.4.3 / 1.4.6.</summary>
    Text,
    /// <summary>Large text (≥ 24px, or ≥ 18.66px bold): 3:1 (AA), 4.5:1 (AAA).</summary>
    LargeText,
    /// <summary>UI components and graphical objects: 3:1. WCAG 2.2 SC 1.4.11.</summary>
    NonText,
    /// <summary>Focus indicator against an adjacent background: 3:1.</summary>
    Focus,
}

public enum ConformanceLevel
{
    AA,
    AAA,
}

/// <summary>An explicit foreground/background pair whose contrast must be checked.</summary>
public sealed record ContrastPair(string Foreground, string Background, ContrastKind Kind = ContrastKind.Text, ConformanceLevel Level = ConformanceLevel.AA);

/// <summary>
/// A naming convention linking backgrounds to foregrounds, e.g. background <c>color.surface.*</c> with foreground <c>color.on-surface.*</c>.
/// Each background must have its foreground (else BDT5004), and the pair's contrast is checked.
/// </summary>
public sealed record ContrastPairing(string BackgroundPattern, string ForegroundPattern, ContrastKind Kind = ContrastKind.Text, ConformanceLevel Level = ConformanceLevel.AA);

/// <summary>Focus indicator color checked against each background matching the patterns (3:1).</summary>
public sealed record FocusIndicatorRule(string Indicator, IReadOnlyList<string> Backgrounds);

public sealed record AccessibilityRules
{
    public IReadOnlyList<ContrastPair> Pairs { get; init; } = [];
    public IReadOnlyList<ContrastPairing> Pairings { get; init; } = [];
    public IReadOnlyList<FocusIndicatorRule> FocusIndicators { get; init; } = [];
    /// <summary>
    /// Token path or hex color of the opaque surface behind translucent backgrounds (typically the page background).
    /// Without it, translucent backgrounds yield "insufficient evidence" (BDT5005) instead of a verdict.
    /// </summary>
    public string? Backdrop { get; init; }
    /// <summary>Also evaluate AAA thresholds for every text pair (BDT5006).</summary>
    public bool IncludeAaa { get; init; }
}

public sealed record ValidationOptions
{
    public static ValidationOptions Default { get; } = new();

    public IReadOnlyList<NamingRule> Naming { get; init; } = [];
    public IReadOnlyList<RequiredToken> RequiredTokens { get; init; } = [];
    public TokenLayerConvention? Layers { get; init; }
    public bool ReportDuplicateValues { get; init; } = true;
    public bool CheckScales { get; init; } = true;
    public bool ReportDeprecatedTokens { get; init; } = true;
    public AccessibilityRules Accessibility { get; init; } = new();
    public DiagnosticPolicy Policy { get; init; } = DiagnosticPolicy.Default;
}
