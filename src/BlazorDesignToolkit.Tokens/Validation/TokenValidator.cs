using System.Globalization;
using System.Text.RegularExpressions;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Validation;

/// <summary>State shared by validation rules during one run.</summary>
public sealed class ValidationContext(ResolvedTokenSet tokens, ValidationOptions options, string? scope)
{
    public ResolvedTokenSet Tokens { get; } = tokens;
    public ValidationOptions Options { get; } = options;
    /// <summary>Evaluation scope (for example "theme=acme mode=dark") attached to every diagnostic.</summary>
    public string? Scope { get; } = scope;
    public DiagnosticBag Bag { get; } = new();
    public List<ContrastResult> ContrastResults { get; } = [];
}

/// <summary>Extension point for custom design-system rules.</summary>
public interface ITokenValidationRule
{
    /// <summary>Rule identifiers this rule may emit (documentation and configuration validation).</summary>
    IReadOnlyList<string> RuleIds { get; }
    void Validate(ValidationContext context);
}

public sealed class ValidationReport(DiagnosticResult result, IReadOnlyList<ContrastResult> contrast, int tokenCount, string? scope)
{
    public DiagnosticResult Result { get; } = result;
    public IReadOnlyList<Diagnostic> Diagnostics => Result.Diagnostics;
    public IReadOnlyList<ContrastResult> ContrastResults { get; } = contrast;
    public int TokenCount { get; } = tokenCount;
    public string? Scope { get; } = scope;
    public bool Failed => Result.Failed;
}

/// <summary>Runs structural diagnostics (from resolution) plus design-system and accessibility rules, then applies the policy.</summary>
public static class TokenValidator
{
    public static IReadOnlyList<ITokenValidationRule> BuiltInRules { get; } =
    [
        new NamingConventionRule(),
        new RequiredTokensRule(),
        new DuplicateValueRule(),
        new ScaleConsistencyRule(),
        new DeprecationRule(),
        new LayerRule(),
        new ContrastRule(),
    ];

    public static ValidationReport Validate(ResolvedTokenSet tokens, ValidationOptions? options = null, IEnumerable<ITokenValidationRule>? additionalRules = null,
        string? scope = null, bool includeResolutionDiagnostics = true)
    {
        options ??= ValidationOptions.Default;
        var context = new ValidationContext(tokens, options, scope);
        foreach (var rule in BuiltInRules.Concat(additionalRules ?? [])) rule.Validate(context);
        var all = (includeResolutionDiagnostics ? tokens.Diagnostics : []).Concat(context.Bag.Items).Concat(options.Policy.Validate());
        if (scope is not null) all = all.Select(d => d.Scope is null ? d.WithScope(scope) : d);
        var result = options.Policy.Apply(all);
        return new ValidationReport(result, context.ContrastResults.Select(c => c with { Scope = scope }).ToList(), tokens.Count, scope);
    }
}

internal sealed partial class NamingConventionRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.NamingConvention.Id];

    public void Validate(ValidationContext context)
    {
        foreach (var rule in context.Options.Naming)
        {
            var segmentRegex = rule.SegmentRegex is null ? null : new Regex(rule.SegmentRegex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var pathRegex = rule.PathRegex is null ? null : new Regex(rule.PathRegex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            foreach (var token in context.Tokens.Tokens)
            {
                var path = token.Path.ToString();
                if (!TokenPathPattern.IsMatch(rule.Pattern, path)) continue;
                if (pathRegex is not null && !pathRegex.IsMatch(path))
                {
                    context.Bag.Report(DiagnosticCatalog.NamingConvention, $"'{path}' does not match the required path pattern.", path, token.Location,
                        expected: rule.PathRegex, actual: path);
                }
                foreach (var segment in token.Path.Segments)
                {
                    if (segment == TokenPath.RootTokenName) continue;
                    if (rule.Case is { } nameCase && !MatchesCase(segment, nameCase))
                    {
                        context.Bag.Report(DiagnosticCatalog.NamingConvention,
                            $"Segment '{segment}' of '{path}' is not {Describe(nameCase)}.", path, token.Location,
                            expected: Describe(nameCase), actual: segment, remediation: $"Rename to '{Convert(segment, nameCase)}'.");
                        break;
                    }
                    if (segmentRegex is not null && !segmentRegex.IsMatch(segment))
                    {
                        context.Bag.Report(DiagnosticCatalog.NamingConvention, $"Segment '{segment}' of '{path}' does not match the configured pattern.", path,
                            token.Location, expected: rule.SegmentRegex, actual: segment);
                        break;
                    }
                }
            }
        }
    }

    internal static bool MatchesCase(string segment, NameCase nameCase) => nameCase switch
    {
        NameCase.KebabCase => Kebab().IsMatch(segment),
        NameCase.CamelCase => Camel().IsMatch(segment) || Digits().IsMatch(segment),
        NameCase.SnakeCase => Snake().IsMatch(segment),
        NameCase.PascalCase => Pascal().IsMatch(segment) || Digits().IsMatch(segment),
        _ => true,
    };

    private static string Describe(NameCase c) => c switch
    {
        NameCase.KebabCase => "kebab-case",
        NameCase.CamelCase => "camelCase",
        NameCase.SnakeCase => "snake_case",
        _ => "PascalCase",
    };

    internal static string Convert(string segment, NameCase c)
    {
        var words = Words().Matches(Export.CssExporter.ToKebab(segment).Replace('_', '-')).Select(static m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0) return segment;
        return c switch
        {
            NameCase.KebabCase => string.Join('-', words),
            NameCase.SnakeCase => string.Join('_', words),
            NameCase.CamelCase => words[0] + string.Concat(words.Skip(1).Select(Capitalize)),
            _ => string.Concat(words.Select(Capitalize)),
        };
    }

    private static string Capitalize(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..];

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex Kebab();
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")] private static partial Regex Camel();
    [GeneratedRegex("^[a-z0-9]+(_[a-z0-9]+)*$")] private static partial Regex Snake();
    [GeneratedRegex("^[A-Z][a-zA-Z0-9]*$")] private static partial Regex Pascal();
    [GeneratedRegex("^[0-9]+$")] private static partial Regex Digits();
    [GeneratedRegex("[A-Za-z0-9]+")] private static partial Regex Words();
}

internal sealed class RequiredTokensRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.MissingRequiredToken.Id, DiagnosticCatalog.RequiredTokenWrongType.Id];

    public void Validate(ValidationContext context)
    {
        foreach (var required in context.Options.RequiredTokens)
        {
            var token = context.Tokens[TokenPath.Parse(required.Path)];
            if (token is null)
            {
                context.Bag.Report(DiagnosticCatalog.MissingRequiredToken, $"Required token '{required.Path}' is not defined.", required.Path,
                    expected: required.Type ?? "any type", remediation: $"Define '{required.Path}'{(required.Type is null ? "" : $" with $type '{required.Type}'")}.");
            }
            else if (required.Type is not null && token.Type != required.Type)
            {
                context.Bag.Report(DiagnosticCatalog.RequiredTokenWrongType, $"Required token '{required.Path}' must be a {required.Type}, but is a {token.Type ?? "token of unknown type"}.",
                    required.Path, token.Location, expected: required.Type, actual: token.Type);
            }
        }
    }
}

internal sealed class DuplicateValueRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.DuplicateValue.Id];

    public void Validate(ValidationContext context)
    {
        if (!context.Options.ReportDuplicateValues) return;
        var groups = context.Tokens.Tokens
            .Where(static t => t.IsValid && !ReferenceSyntax.IsReference(t.RawValue) && !ContainsReference(t.RawValue))
            // Only tokens in the same top-level group are compared: equal values across scales (e.g. a radius and a spacing step)
            // are usually coincidences, while duplicates inside one palette usually indicate a missing alias.
            .GroupBy(static t => t.Path.Segments[0] + "|" + t.Type + "|" + t.Value!.ToSourceNode().ToJson(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var list = group.ToList();
            if (list.Count < 2) continue;
            var first = list[0];
            foreach (var dup in list.Skip(1))
            {
                context.Bag.Report(DiagnosticCatalog.DuplicateValue,
                    $"'{dup.Path}' declares the same {dup.Type} value as '{first.Path}'.", dup.Path.ToString(), dup.Location,
                    actual: dup.Value!.ToSourceNode().ToJson(), remediation: $"Alias it: \"$value\": \"{{{first.Path}}}\", if both should always match.");
            }
        }
    }

    private static bool ContainsReference(Json.SourceNode node) => node switch
    {
        Json.SourceObject o => ReferenceSyntax.IsReference(o) || o.Properties.Any(p => ContainsReference(p.Value)),
        Json.SourceArray a => a.Items.Any(ContainsReference),
        _ => ReferenceSyntax.IsReference(node),
    };
}

internal sealed class ScaleConsistencyRule : ITokenValidationRule
{
    private static readonly string[] s_sizeOrder =
        ["4xs", "3xs", "xxxs", "2xs", "xxs", "xs", "s", "sm", "m", "md", "base", "l", "lg", "xl", "2xl", "xxl", "3xl", "xxxl", "4xl", "5xl", "6xl"];

    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.InconsistentScale.Id];

    public void Validate(ValidationContext context)
    {
        if (!context.Options.CheckScales) return;
        foreach (var group in context.Tokens.Tokens.Where(static t => t.IsValid && !t.Path.IsRootToken).GroupBy(static t => t.Path.Parent))
        {
            var members = group.ToList();
            if (members.Count < 3 || members.Select(static m => m.Type).Distinct().Count() != 1) continue;
            var ranks = members.Select(m => (Token: m, Rank: Rank(m.Path.Name))).ToList();
            if (ranks.Any(static r => r.Rank is null)) continue;
            var measured = ranks.Select(r => (r.Token, Rank: r.Rank!.Value, Measure: Measure(r.Token))).ToList();
            if (measured.Any(static m => m.Measure is null)) continue;
            var ordered = measured.OrderBy(static m => m.Rank).ToList();
            // Determine the dominant direction, then flag steps against it.
            var increases = 0;
            var decreases = 0;
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Measure > ordered[i - 1].Measure) increases++;
                else if (ordered[i].Measure < ordered[i - 1].Measure) decreases++;
            }
            if (increases == 0 || decreases == 0) continue;
            var increasing = increases >= decreases;
            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                var wrong = increasing ? cur.Measure < prev.Measure : cur.Measure > prev.Measure;
                if (!wrong) continue;
                context.Bag.Report(DiagnosticCatalog.InconsistentScale,
                    $"Scale '{group.Key}' is {(increasing ? "increasing" : "decreasing")}, but '{cur.Token.Path.Name}' ({Fmt(cur.Measure!.Value)}) breaks the order after '{prev.Token.Path.Name}' ({Fmt(prev.Measure!.Value)}).",
                    cur.Token.Path.ToString(), cur.Token.Location, expected: $"{(increasing ? ">=" : "<=")} {Fmt(prev.Measure!.Value)}", actual: Fmt(cur.Measure!.Value));
            }
        }
    }

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double? Rank(string name)
    {
        if (double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
        var index = Array.IndexOf(s_sizeOrder, name.ToLowerInvariant());
        return index >= 0 ? index : null;
    }

    /// <summary>Comparable magnitude: px for dimensions (rem assumed 16px only when all members share units is not required), ms, weight, or OKLCH lightness for colors.</summary>
    private static double? Measure(ResolvedToken token) => token.Value switch
    {
        DimensionValue d => d.Unit switch { "px" => d.Value, "rem" => d.Value * 16, _ => null },
        DurationValue d => d.Milliseconds,
        NumberValue n => n.Value,
        FontWeightValue w => w.Weight,
        ColorValue c when !c.HasNoneComponent => ColorMath.Convert(c.ColorSpace, c.Components, ColorSpaces.Oklch)[0],
        _ => null,
    };
}

internal sealed class DeprecationRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.DeprecatedReference.Id, DiagnosticCatalog.DeprecatedToken.Id];

    public void Validate(ValidationContext context)
    {
        foreach (var token in context.Tokens.Tokens)
        {
            if (token.IsDeprecated && context.Options.ReportDeprecatedTokens)
            {
                context.Bag.Report(DiagnosticCatalog.DeprecatedToken,
                    $"'{token.Path}' is deprecated{(token.Deprecation!.Explanation is { } e ? ": " + e : ".")}", token.Path.ToString(), token.Location);
            }
            if (token.IsDeprecated) continue;
            foreach (var reference in token.References)
            {
                if (context.Tokens[reference] is { IsDeprecated: true } target)
                {
                    context.Bag.Report(DiagnosticCatalog.DeprecatedReference,
                        $"'{token.Path}' references deprecated token '{reference}'{(target.Deprecation!.Explanation is { } e ? ": " + e : ".")}",
                        token.Path.ToString(), token.Location, referenceChain: [token.Path.ToString(), reference.ToString()]);
                }
            }
        }
    }
}

internal sealed class LayerRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds => [DiagnosticCatalog.LayerViolation.Id];

    private enum Layer { None, Primitive, Semantic, Component }

    public void Validate(ValidationContext context)
    {
        if (context.Options.Layers is not { } layers) return;
        Layer Classify(TokenPath path)
        {
            var text = path.ToString();
            if (layers.Component.Any(p => TokenPathPattern.IsMatch(p, text))) return Layer.Component;
            if (layers.Semantic.Any(p => TokenPathPattern.IsMatch(p, text))) return Layer.Semantic;
            if (layers.Primitive.Any(p => TokenPathPattern.IsMatch(p, text))) return Layer.Primitive;
            return Layer.None;
        }
        foreach (var token in context.Tokens.Tokens)
        {
            var layer = Classify(token.Path);
            var path = token.Path.ToString();
            if (layer == Layer.Semantic && layers.SemanticMustAlias && token.References.Count == 0)
            {
                context.Bag.Report(DiagnosticCatalog.LayerViolation, $"Semantic token '{path}' holds a raw value instead of referencing a primitive.",
                    path, token.Location, expected: "alias to a primitive token", actual: token.RawValue.ToJson(),
                    remediation: "Add the value to the primitive palette and alias it from the semantic token.");
            }
            foreach (var reference in token.References)
            {
                var targetLayer = Classify(reference);
                if (layer == Layer.Primitive && targetLayer is Layer.Semantic or Layer.Component)
                {
                    context.Bag.Report(DiagnosticCatalog.LayerViolation, $"Primitive token '{path}' references the {targetLayer.ToString().ToLowerInvariant()} token '{reference}'.",
                        path, token.Location, referenceChain: [path, reference.ToString()], remediation: "Primitives must not depend on higher layers.");
                }
                else if (layer == Layer.Semantic && targetLayer == Layer.Component)
                {
                    context.Bag.Report(DiagnosticCatalog.LayerViolation, $"Semantic token '{path}' references the component token '{reference}'.",
                        path, token.Location, referenceChain: [path, reference.ToString()]);
                }
                else if (layer == Layer.Component && targetLayer == Layer.Primitive && layers.ComponentShouldUseSemantic)
                {
                    context.Bag.Report(DiagnosticCatalog.LayerViolation, $"Component token '{path}' references the primitive '{reference}' directly.",
                        path, token.Location, referenceChain: [path, reference.ToString()],
                        remediation: "Reference a semantic token so that themes can change the meaning in one place.");
                }
            }
        }
    }
}

internal sealed class ContrastRule : ITokenValidationRule
{
    public IReadOnlyList<string> RuleIds =>
    [
        DiagnosticCatalog.TextContrast.Id, DiagnosticCatalog.NonTextContrast.Id, DiagnosticCatalog.FocusIndicatorContrast.Id,
        DiagnosticCatalog.MissingColorPair.Id, DiagnosticCatalog.ContrastIndeterminate.Id, DiagnosticCatalog.EnhancedTextContrast.Id,
        DiagnosticCatalog.InvalidContrastPair.Id,
    ];

    public void Validate(ValidationContext context)
    {
        var rules = context.Options.Accessibility;
        ColorValue? backdrop = null;
        if (rules.Backdrop is { } b)
        {
            backdrop = b.StartsWith('#') && SrgbColor.TryParseHex(b, out var hex) ? ColorValue.FromSrgb(hex) : ResolveColor(context, b, "backdrop");
        }

        var evaluated = new HashSet<(string, string, ContrastKind, ConformanceLevel)>();
        foreach (var pair in rules.Pairs) Check(context, pair.Foreground, pair.Background, pair.Kind, pair.Level, backdrop, evaluated);

        foreach (var pairing in rules.Pairings)
        {
            foreach (var token in context.Tokens.Tokens.Where(static t => t.Type == TokenTypes.Color))
            {
                var path = token.Path.ToString();
                if (!TokenPathPattern.TryCapture(pairing.BackgroundPattern, path, out var capture)) continue;
                var foreground = pairing.ForegroundPattern.Replace("*", capture, StringComparison.Ordinal);
                if (!context.Tokens.Contains(TokenPath.Parse(foreground)))
                {
                    context.Bag.Report(DiagnosticCatalog.MissingColorPair, $"Background '{path}' has no foreground '{foreground}'.", path, token.Location,
                        expected: foreground, remediation: $"Define '{foreground}' so text on '{path}' has a tested color.");
                    continue;
                }
                Check(context, foreground, path, pairing.Kind, pairing.Level, backdrop, evaluated);
            }
        }

        foreach (var focus in rules.FocusIndicators)
        {
            var backgrounds = context.Tokens.Tokens
                .Where(t => t.Type == TokenTypes.Color && focus.Backgrounds.Any(p => TokenPathPattern.IsMatch(p, t.Path.ToString())))
                .Select(static t => t.Path.ToString()).ToList();
            if (backgrounds.Count == 0)
            {
                context.Bag.Report(DiagnosticCatalog.InvalidContrastPair, $"Focus indicator rule for '{focus.Indicator}' matches no background tokens.", focus.Indicator,
                    expected: string.Join(", ", focus.Backgrounds));
            }
            foreach (var background in backgrounds) Check(context, focus.Indicator, background, ContrastKind.Focus, ConformanceLevel.AA, backdrop, evaluated);
        }
    }

    private static void Check(ValidationContext context, string foreground, string background, ContrastKind kind, ConformanceLevel level, ColorValue? backdrop,
        HashSet<(string, string, ContrastKind, ConformanceLevel)> evaluated)
    {
        if (!evaluated.Add((foreground, background, kind, level))) return;
        var fg = ResolveColor(context, foreground, "foreground");
        var bg = ResolveColor(context, background, "background");
        if (fg is null || bg is null) return;
        var result = ContrastEvaluator.Evaluate(foreground, fg, background, bg, kind, level, backdrop);
        context.ContrastResults.Add(result);
        var location = context.Tokens[TokenPath.Parse(foreground)]?.Location;
        if (result.Passed is null)
        {
            context.Bag.Report(DiagnosticCatalog.ContrastIndeterminate,
                $"Contrast of '{foreground}' on '{background}' cannot be determined: {result.IndeterminateReason}.", foreground, location,
                expected: $">= {result.Required.ToString(CultureInfo.InvariantCulture)}:1",
                remediation: "Configure an opaque backdrop, use in-gamut colors, or verify the rendered result manually.");
        }
        else if (result.Passed == false)
        {
            var descriptor = (kind, level) switch
            {
                (ContrastKind.Text or ContrastKind.LargeText, ConformanceLevel.AAA) => DiagnosticCatalog.EnhancedTextContrast,
                (ContrastKind.Text or ContrastKind.LargeText, _) => DiagnosticCatalog.TextContrast,
                (ContrastKind.Focus, _) => DiagnosticCatalog.FocusIndicatorContrast,
                _ => DiagnosticCatalog.NonTextContrast,
            };
            context.Bag.Report(descriptor,
                $"'{foreground}' on '{background}' has a contrast ratio of {result.RatioText} ({DescribeKind(kind)}, {level}); at least {result.Required.ToString(CultureInfo.InvariantCulture)}:1 is required.",
                foreground, location, expected: $">= {result.Required.ToString(CultureInfo.InvariantCulture)}:1", actual: result.RatioText,
                remediation: result.SuggestedForeground is { } s ? $"A foreground of about {s} (same hue, adjusted lightness) would pass." : "Adjust the lightness of either color.");
        }

        if (context.Options.Accessibility.IncludeAaa && level == ConformanceLevel.AA && kind is ContrastKind.Text or ContrastKind.LargeText)
        {
            Check(context, foreground, background, kind, ConformanceLevel.AAA, backdrop, evaluated);
        }
    }

    private static string DescribeKind(ContrastKind kind) => kind switch
    {
        ContrastKind.Text => "text",
        ContrastKind.LargeText => "large text",
        ContrastKind.Focus => "focus indicator",
        _ => "non-text",
    };

    private static ColorValue? ResolveColor(ValidationContext context, string path, string role)
    {
        var token = context.Tokens[TokenPath.Parse(path)];
        if (token is null)
        {
            context.Bag.Report(DiagnosticCatalog.InvalidContrastPair, $"The {role} '{path}' of a contrast rule is not defined.", path);
            return null;
        }
        if (token.Type != TokenTypes.Color)
        {
            context.Bag.Report(DiagnosticCatalog.InvalidContrastPair, $"The {role} '{path}' of a contrast rule is a {token.Type}, not a color.", path, token.Location,
                expected: "color", actual: token.Type);
            return null;
        }
        return token.Value as ColorValue; // invalid tokens were already reported by resolution
    }
}
