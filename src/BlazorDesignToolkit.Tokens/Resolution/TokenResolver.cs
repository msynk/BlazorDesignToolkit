using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Resolution;

public sealed record TokenResolutionOptions
{
    public static TokenResolutionOptions Default { get; } = new();

    /// <summary>Maximum alias chain length before resolution stops with <c>BDT3008</c>. Guards against pathological input.</summary>
    public int MaxReferenceDepth { get; init; } = 64;

    public ValueParseOptions Values { get; init; } = ValueParseOptions.Default;
}

/// <summary>A fully resolved token: its type, resolved JSON value and typed value, plus reference metadata.</summary>
public sealed class ResolvedToken
{
    internal ResolvedToken(FlatToken flat, string? type, SourceNode? resolvedValue, TokenValue? value,
        TokenPath? aliasOf, IReadOnlyList<TokenPath> references)
    {
        Flat = flat;
        Type = type;
        ResolvedValue = resolvedValue;
        Value = value;
        AliasOf = aliasOf;
        References = references;
    }

    internal FlatToken Flat { get; }
    public TokenPath Path => Flat.Path;
    public TokenDefinition Definition => Flat.Definition;
    /// <summary>The determined type (explicit, via reference, or inherited from a group). Null when undeterminable.</summary>
    public string? Type { get; }
    /// <summary>The authored value, possibly containing references.</summary>
    public SourceNode RawValue => Flat.Definition.Value;
    /// <summary>The value with every reference replaced by the referenced value. Null when resolution failed.</summary>
    public SourceNode? ResolvedValue { get; }
    /// <summary>The typed value. Null when the token is invalid.</summary>
    public TokenValue? Value { get; }
    /// <summary>When the whole value is a reference to another token, that token's path.</summary>
    public TokenPath? AliasOf { get; }
    /// <summary>Tokens referenced directly by this token's value (any depth inside the value), in first-use order.</summary>
    public IReadOnlyList<TokenPath> References { get; }
    public string? Description => Flat.Definition.Description;
    public TokenDeprecation? Deprecation => Flat.Deprecation;
    public bool IsDeprecated => Deprecation?.IsDeprecated == true;
    public SourceObject? Extensions => Flat.Definition.Extensions;
    public SourceLocation? Location => Flat.Definition.Location;
    public TokenPath? InheritedFrom => Flat.InheritedFrom;
    public bool IsValid => Value is not null;
    public bool IsAlias => AliasOf is not null;

    public override string ToString() => $"{Path} ({Type ?? "?"}) = {ResolvedValue?.ToJson() ?? "<unresolved>"}";
}

/// <summary>The result of resolving a token document: every token with its resolved value, plus all diagnostics.</summary>
public sealed class ResolvedTokenSet
{
    private readonly Dictionary<TokenPath, ResolvedToken> _byPath;
    private Dictionary<TokenPath, List<TokenPath>>? _dependents;

    internal ResolvedTokenSet(IReadOnlyList<ResolvedToken> tokens, IReadOnlySet<TokenPath> groups, IReadOnlyList<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        Groups = groups;
        Diagnostics = diagnostics;
        _byPath = new Dictionary<TokenPath, ResolvedToken>(tokens.Count);
        foreach (var t in tokens) _byPath[t.Path] = t;
    }

    /// <summary>Tokens in document order.</summary>
    public IReadOnlyList<ResolvedToken> Tokens { get; }
    public IReadOnlySet<TokenPath> Groups { get; }
    /// <summary>All diagnostics from parsing, flattening, resolution and value validation, sorted deterministically.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
    public int Count => Tokens.Count;

    public ResolvedToken? this[TokenPath path] => _byPath.GetValueOrDefault(path);

    public bool TryGet(TokenPath path, out ResolvedToken token) => _byPath.TryGetValue(path, out token!);

    public bool Contains(TokenPath path) => _byPath.ContainsKey(path);

    /// <summary>Tokens whose values reference <paramref name="path"/> directly.</summary>
    public IReadOnlyList<TokenPath> GetDependents(TokenPath path)
    {
        if (_dependents is null)
        {
            var map = new Dictionary<TokenPath, List<TokenPath>>();
            foreach (var t in Tokens)
            {
                foreach (var r in t.References)
                {
                    if (!map.TryGetValue(r, out var list)) map[r] = list = [];
                    list.Add(t.Path);
                }
            }
            _dependents = map;
        }
        return _dependents.TryGetValue(path, out var deps) ? deps : [];
    }

    /// <summary>Follows whole-value aliases to the token that holds the literal value.</summary>
    public IReadOnlyList<TokenPath> GetAliasChain(TokenPath path)
    {
        var chain = new List<TokenPath>();
        var seen = new HashSet<TokenPath>();
        var current = this[path];
        while (current?.AliasOf is { } next && seen.Add(current.Path))
        {
            chain.Add(next);
            current = this[next];
        }
        return chain;
    }
}

/// <summary>
/// Resolves curly-brace aliases and <c>$ref</c> JSON Pointer references (DTCG 2025.10 §7), determines token types (§5.2.2),
/// detects cycles with full reference chains, and validates values. Results are independent of input ordering.
/// </summary>
public static class TokenResolver
{
    public static ResolvedTokenSet Resolve(TokenDocument document, TokenResolutionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var flat = TokenFlattener.Flatten(document);
        return Resolve(flat, options, cancellationToken, document.Diagnostics);
    }

    public static ResolvedTokenSet Resolve(FlatTokenSet set, TokenResolutionOptions? options = null, CancellationToken cancellationToken = default,
        IEnumerable<Diagnostic>? priorDiagnostics = null)
    {
        options ??= TokenResolutionOptions.Default;
        var engine = new Engine(set, options, cancellationToken);
        // Resolve in ordinal path order so that diagnostics and memoization never depend on document order.
        foreach (var path in set.Paths.Order()) engine.Resolve(path);
        var tokens = set.Tokens.Select(t => engine.Results[t.Path]).ToList();
        var bag = new DiagnosticBag();
        if (priorDiagnostics is not null) bag.AddRange(priorDiagnostics);
        bag.AddRange(set.Diagnostics);
        bag.AddRange(engine.Bag.Items);
        return new ResolvedTokenSet(tokens, set.Groups, bag.ToSortedList());
    }

    /// <summary>Convenience: parse JSON text and resolve it.</summary>
    public static ResolvedTokenSet Resolve(string json, JsonParseOptions? parseOptions = null, TokenResolutionOptions? options = null, CancellationToken cancellationToken = default)
        => Resolve(DtcgReader.Read(json, parseOptions, cancellationToken), options, cancellationToken);

    private sealed class Engine(FlatTokenSet set, TokenResolutionOptions options, CancellationToken cancellationToken)
    {
        private readonly HashSet<TokenPath> _visiting = [];
        private readonly List<TokenPath> _stack = [];
        private readonly HashSet<TokenPath> _cycleMembers = [];
        private readonly HashSet<string> _reportedCycles = new(StringComparer.Ordinal);

        public Dictionary<TokenPath, ResolvedToken> Results { get; } = [];
        public DiagnosticBag Bag { get; } = new();

        public ResolvedToken Resolve(TokenPath path)
        {
            if (Results.TryGetValue(path, out var done)) return done;
            cancellationToken.ThrowIfCancellationRequested();
            var flat = set[path]!;
            var def = flat.Definition;
            _visiting.Add(path);
            _stack.Add(path);

            var ctx = new ValueContext(path);
            string? type = def.Type;
            TokenPath? aliasOf = null;
            SourceNode? resolved;

            if (_stack.Count > options.MaxReferenceDepth)
            {
                Bag.Report(DiagnosticCatalog.ReferenceDepthExceeded,
                    $"Reference chain to '{path}' exceeds the maximum depth of {options.MaxReferenceDepth}.", path.ToString(), def.Location,
                    expected: $"<= {options.MaxReferenceDepth}", actual: _stack.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    referenceChain: _stack.Select(static p => p.ToString()).ToList());
                resolved = null;
                ctx.Failed = true;
            }
            else
            {
                // Whole-value references determine the type when no explicit $type is declared (§5.2.2).
                var wholeTarget = WholeValueTarget(def.Value);
                if (wholeTarget is not null) aliasOf = wholeTarget;
                var expected = type ?? (wholeTarget is null ? flat.GroupType : null);
                resolved = ResolveNode(def.Value, expected, ctx, out var referencedType);
                if (type is null && wholeTarget is not null) type = referencedType;
                type ??= flat.GroupType;
            }

            TokenValue? value = null;
            if (!ctx.Failed && resolved is not null)
            {
                if (type is null)
                {
                    Bag.Report(DiagnosticCatalog.TypeUndetermined,
                        $"The type of '{path}' cannot be determined: it has no $type, is not an alias, and no parent group declares $type.",
                        path.ToString(), def.Location);
                }
                else if (TokenTypes.IsStandard(type))
                {
                    value = TokenValueParser.Parse(type, resolved, path.ToString(), Bag, options.Values);
                }
                // Unknown types were already reported by the reader (BDT2004); the token stays unresolved.
            }

            var result = new ResolvedToken(flat, type, ctx.Failed ? null : resolved, value, aliasOf, ctx.References);
            _stack.RemoveAt(_stack.Count - 1);
            _visiting.Remove(path);
            Results[path] = result;
            return result;
        }

        private static TokenPath? WholeValueTarget(SourceNode value)
        {
            if (value is SourceString s && ReferenceSyntax.TryParseAlias(s.Value, out var target)) return target;
            if (ReferenceSyntax.TryGetPointer(value, out var pointer) && JsonPointer.TryParse(pointer, out var segments, out _)
                && segments.Count > 0 && segments[^1] == "$value" && segments.Count(static s => s == "$value") == 1)
            {
                return new TokenPath(segments.Take(segments.Count - 1));
            }
            return null;
        }

        private SourceNode? ResolveNode(SourceNode node, string? expectedType, ValueContext ctx, out string? referencedType)
        {
            referencedType = null;
            switch (node)
            {
                case SourceString s when ReferenceSyntax.TryParseAlias(s.Value, out var target):
                    return ResolveTarget(target, null, expectedType, ctx, s.Location, s.Value, out referencedType);

                case SourceString s when ReferenceSyntax.LooksLikeMalformedAlias(s.Value) && expectedType != TokenTypes.FontFamily:
                    Bag.Report(DiagnosticCatalog.InvalidReferenceSyntax,
                        $"'{s.Value}' in '{ctx.Path}' is not a valid reference. References must be the entire string, e.g. '{{color.base.blue}}'.",
                        ctx.Path.ToString(), s.Location, expected: "{group.token}", actual: s.Value);
                    ctx.Failed = true;
                    return null;

                case SourceObject o when o.ContainsKey("$ref"):
                    {
                        if (!ReferenceSyntax.TryGetPointer(o, out var pointer))
                        {
                            Bag.Report(DiagnosticCatalog.InvalidJsonPointer,
                                $"A $ref object in '{ctx.Path}' must contain only a string '$ref' property.", ctx.Path.ToString(), o.Location, actual: o.ToJson());
                            ctx.Failed = true;
                            return null;
                        }
                        return ResolvePointer(pointer, expectedType, ctx, o.Location, out referencedType);
                    }

                case SourceObject o:
                    {
                        var props = new List<SourceProperty>(o.Count);
                        foreach (var p in o.Properties)
                        {
                            var child = ResolveNode(p.Value, CompositeSchema.PropertyType(expectedType, p.Name), ctx, out _);
                            if (child is null) return null;
                            props.Add(p with { Value = child });
                        }
                        return new SourceObject(props, o.Location);
                    }

                case SourceArray a:
                    {
                        var items = new List<SourceNode>(a.Items.Count);
                        var itemType = CompositeSchema.ItemType(expectedType);
                        foreach (var item in a.Items)
                        {
                            var child = ResolveNode(item, itemType, ctx, out _);
                            if (child is null) return null;
                            items.Add(child);
                        }
                        return new SourceArray(items, a.Location);
                    }

                default:
                    return node;
            }
        }

        private SourceNode? ResolvePointer(string pointer, string? expectedType, ValueContext ctx, SourceLocation? location, out string? referencedType)
        {
            referencedType = null;
            if (!JsonPointer.TryParse(pointer, out var segments, out var error))
            {
                Bag.Report(DiagnosticCatalog.InvalidJsonPointer, $"Invalid JSON Pointer '{pointer}' in '{ctx.Path}': {error}", ctx.Path.ToString(), location, actual: pointer);
                ctx.Failed = true;
                return null;
            }
            var valueIndex = segments.ToList().IndexOf("$value");
            if (valueIndex < 0)
            {
                var path = new TokenPath(segments);
                if (set.ContainsToken(path))
                {
                    Bag.Report(DiagnosticCatalog.InvalidJsonPointer,
                        $"JSON Pointer '{pointer}' in '{ctx.Path}' targets the token object; append '/$value' to reference its value.",
                        ctx.Path.ToString(), location, expected: pointer + "/$value", actual: pointer);
                }
                else ReportMissingTarget(path, ctx, location, pointer);
                ctx.Failed = true;
                return null;
            }
            var tokenPath = new TokenPath(segments.Take(valueIndex));
            var subPath = segments.Skip(valueIndex + 1).ToList();
            var targetValue = ResolveTarget(tokenPath, subPath, subPath.Count == 0 ? expectedType : null, ctx, location, pointer, out referencedType);
            if (targetValue is null) return null;
            if (subPath.Count == 0) return targetValue;
            var sub = JsonPointer.Navigate(targetValue, subPath);
            if (sub is null)
            {
                Bag.Report(DiagnosticCatalog.InvalidJsonPointer,
                    $"JSON Pointer '{pointer}' in '{ctx.Path}' does not resolve: '{tokenPath}' has no '{string.Join('/', subPath)}' in its value.",
                    ctx.Path.ToString(), location, actual: pointer);
                ctx.Failed = true;
                return null;
            }
            referencedType = null;
            return sub;
        }

        private SourceNode? ResolveTarget(TokenPath target, IReadOnlyList<string>? subPath, string? expectedType, ValueContext ctx,
            SourceLocation? location, string referenceText, out string? referencedType)
        {
            referencedType = null;
            if (!set.ContainsToken(target))
            {
                ReportMissingTarget(target, ctx, location, referenceText);
                ctx.Failed = true;
                return null;
            }
            if (_visiting.Contains(target))
            {
                ReportCycle(target, location);
                ctx.Failed = true;
                return null;
            }
            var resolved = Resolve(target);
            ctx.AddReference(target);
            if (resolved.ResolvedValue is null || resolved.Value is null)
            {
                // Members of a cycle are already covered by the BDT3002 report; everything else points at the root cause.
                if (!_cycleMembers.Contains(ctx.Path))
                {
                    Bag.Report(DiagnosticCatalog.DependsOnInvalidToken,
                        $"'{ctx.Path}' cannot be resolved because '{target}' is invalid.", ctx.Path.ToString(), location,
                        referenceChain: [ctx.Path.ToString(), target.ToString()]);
                }
                ctx.Failed = true;
                return null;
            }
            referencedType = resolved.Type;
            if (expectedType is not null && TokenTypes.IsStandard(expectedType) && resolved.Type is not null && resolved.Type != expectedType)
            {
                Bag.Report(DiagnosticCatalog.ReferenceTypeMismatch,
                    $"'{referenceText}' in '{ctx.Path}' resolves to a {resolved.Type} token, but a {expectedType} is required here.",
                    ctx.Path.ToString(), location, expected: expectedType, actual: resolved.Type,
                    referenceChain: [ctx.Path.ToString(), target.ToString()]);
                ctx.Failed = true;
                return null;
            }
            return resolved.ResolvedValue;
        }

        private void ReportMissingTarget(TokenPath target, ValueContext ctx, SourceLocation? location, string referenceText)
        {
            if (set.Groups.Contains(target))
            {
                var hasRoot = set.ContainsToken(target.Append(TokenPath.RootTokenName));
                Bag.Report(DiagnosticCatalog.ReferenceTargetsGroup,
                    $"'{referenceText}' in '{ctx.Path}' references the group '{target}', not a token.", ctx.Path.ToString(), location,
                    actual: referenceText,
                    remediation: hasRoot ? $"Reference the group's root token: '{{{target}.$root}}'." : "Reference a token inside the group.");
                return;
            }
            var suggestion = Suggest(target);
            Bag.Report(DiagnosticCatalog.MissingReference,
                $"'{referenceText}' in '{ctx.Path}' references '{target}', which does not exist.", ctx.Path.ToString(), location,
                actual: target.ToString(), remediation: suggestion is null ? "Define the token or fix the reference." : $"Did you mean '{{{suggestion}}}'?",
                referenceChain: [ctx.Path.ToString(), target.ToString()]);
        }

        private void ReportCycle(TokenPath target, SourceLocation? location)
        {
            var start = _stack.IndexOf(target);
            var cycle = _stack.Skip(start).Append(target).ToList();
            foreach (var p in cycle) _cycleMembers.Add(p);
            // Report each cycle once, regardless of which member was resolved first.
            var key = string.Join(">", cycle.Take(cycle.Count - 1).Select(static p => p.ToString()).Order(StringComparer.Ordinal));
            if (!_reportedCycles.Add(key)) return;
            var chain = cycle.Select(static p => p.ToString()).ToList();
            Bag.Report(DiagnosticCatalog.CircularReference, $"Circular reference: {string.Join(" -> ", chain)}.", target.ToString(),
                location ?? set[target]?.Definition.Location, referenceChain: chain);
        }

        private string? Suggest(TokenPath missing)
        {
            var text = missing.ToString();
            string? best = null;
            var bestDistance = int.MaxValue;
            foreach (var candidate in set.Paths)
            {
                var c = candidate.ToString();
                if (Math.Abs(c.Length - text.Length) > 3) continue;
                var d = Levenshtein(text, c);
                if (d < bestDistance || (d == bestDistance && string.CompareOrdinal(c, best) < 0))
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return bestDistance <= Math.Max(2, text.Length / 5) ? best : null;
        }

        private static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                }
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }
    }

    private sealed class ValueContext(TokenPath path)
    {
        private readonly List<TokenPath> _references = [];
        public TokenPath Path { get; } = path;
        public bool Failed { get; set; }
        public IReadOnlyList<TokenPath> References => _references;

        public void AddReference(TokenPath target)
        {
            if (!_references.Contains(target)) _references.Add(target);
        }
    }
}

/// <summary>Sub-value types of composite tokens, used to type-check references inside composite values (§9).</summary>
public static class CompositeSchema
{
    internal const string DimensionList = "dimension[]";
    internal const string NumberList = "number[]";
    internal const string GradientStop = "gradient-stop";

    public static string? PropertyType(string? parentType, string property) => (parentType, property) switch
    {
        (TokenTypes.Border, "color") => TokenTypes.Color,
        (TokenTypes.Border, "width") => TokenTypes.Dimension,
        (TokenTypes.Border, "style") => TokenTypes.StrokeStyle,
        (TokenTypes.Transition, "duration") => TokenTypes.Duration,
        (TokenTypes.Transition, "delay") => TokenTypes.Duration,
        (TokenTypes.Transition, "timingFunction") => TokenTypes.CubicBezier,
        (TokenTypes.Shadow, "color") => TokenTypes.Color,
        (TokenTypes.Shadow, "offsetX" or "offsetY" or "blur" or "spread") => TokenTypes.Dimension,
        (TokenTypes.Typography, "fontFamily") => TokenTypes.FontFamily,
        (TokenTypes.Typography, "fontSize" or "letterSpacing") => TokenTypes.Dimension,
        (TokenTypes.Typography, "fontWeight") => TokenTypes.FontWeight,
        (TokenTypes.Typography, "lineHeight") => TokenTypes.Number,
        (TokenTypes.StrokeStyle, "dashArray") => DimensionList,
        (GradientStop, "color") => TokenTypes.Color,
        (GradientStop, "position") => TokenTypes.Number,
        (TokenTypes.Color, "components") => NumberList,
        (TokenTypes.Color, "alpha") => TokenTypes.Number,
        (TokenTypes.Dimension or TokenTypes.Duration, "value") => TokenTypes.Number,
        _ => null,
    };

    public static string? ItemType(string? parentType) => parentType switch
    {
        TokenTypes.Shadow => TokenTypes.Shadow,
        TokenTypes.Gradient => GradientStop,
        DimensionList => TokenTypes.Dimension,
        NumberList => TokenTypes.Number,
        TokenTypes.CubicBezier => TokenTypes.Number,
        _ => null,
    };
}
