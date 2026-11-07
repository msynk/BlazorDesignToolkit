using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens;

/// <summary>The thirteen token types defined by DTCG 2025.10 (§8 and §9). Type names are case-sensitive.</summary>
public static class TokenTypes
{
    public const string Color = "color";
    public const string Dimension = "dimension";
    public const string FontFamily = "fontFamily";
    public const string FontWeight = "fontWeight";
    public const string Duration = "duration";
    public const string CubicBezier = "cubicBezier";
    public const string Number = "number";
    public const string StrokeStyle = "strokeStyle";
    public const string Border = "border";
    public const string Transition = "transition";
    public const string Shadow = "shadow";
    public const string Gradient = "gradient";
    public const string Typography = "typography";

    public static FrozenSet<string> All { get; } = new[]
    {
        Color, Dimension, FontFamily, FontWeight, Duration, CubicBezier, Number,
        StrokeStyle, Border, Transition, Shadow, Gradient, Typography,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static FrozenSet<string> Composite { get; } = new[] { StrokeStyle, Border, Transition, Shadow, Gradient, Typography }
        .ToFrozenSet(StringComparer.Ordinal);

    public static bool IsStandard(string type) => All.Contains(type);
}

/// <summary>$deprecated metadata. <see cref="IsDeprecated"/> false explicitly un-deprecates a token inside a deprecated group.</summary>
public sealed record TokenDeprecation(bool IsDeprecated, string? Explanation = null)
{
    public static TokenDeprecation NotDeprecated { get; } = new(false);
    public static TokenDeprecation Deprecated { get; } = new(true);
}

/// <summary>Parses the two DTCG reference syntaxes: curly-brace aliases and <c>$ref</c> JSON Pointer objects.</summary>
public static class ReferenceSyntax
{
    /// <summary>True when <paramref name="value"/> is exactly a curly-brace reference such as <c>{color.base.blue}</c>.</summary>
    public static bool TryParseAlias(string value, out TokenPath path)
    {
        path = default;
        if (value.Length < 3 || value[0] != '{' || value[^1] != '}') return false;
        var inner = value.AsSpan(1, value.Length - 2);
        if (inner.IndexOfAny('{', '}') >= 0) return false;
        var segments = inner.ToString().Split('.');
        if (segments.Any(static s => s.Length == 0)) return false;
        path = new TokenPath(segments);
        return true;
    }

    /// <summary>True when the string contains brace characters but is not a valid whole-value alias.</summary>
    public static bool LooksLikeMalformedAlias(string value)
        => (value.Contains('{') || value.Contains('}')) && !TryParseAlias(value, out _);

    public static bool IsAlias(SourceNode node) => node is SourceString s && TryParseAlias(s.Value, out _);

    /// <summary>True when <paramref name="node"/> is an object whose only property is a string <c>$ref</c>.</summary>
    public static bool TryGetPointer(SourceNode node, [NotNullWhen(true)] out string? pointer)
    {
        pointer = null;
        if (node is SourceObject { Count: 1 } o && o.TryGetProperty("$ref", out var r) && r is SourceString s)
        {
            pointer = s.Value;
            return true;
        }
        return false;
    }

    public static bool IsReference(SourceNode node) => IsAlias(node) || TryGetPointer(node, out _);

    public static string FormatAlias(TokenPath path) => "{" + path + "}";
}

/// <summary>Common members of authored tokens and groups.</summary>
public abstract class TokenNode
{
    private protected TokenNode(string name, TokenPath path) { Name = name; Path = path; }

    public string Name { get; }
    public TokenPath Path { get; }
    public string? Description { get; init; }
    public TokenDeprecation? Deprecation { get; init; }
    /// <summary>The raw <c>$extensions</c> object, preserved verbatim.</summary>
    public SourceObject? Extensions { get; init; }
    /// <summary>Unrecognised '$'-prefixed properties, preserved for round-tripping.</summary>
    public IReadOnlyList<SourceProperty> UnknownReservedProperties { get; init; } = [];
    public SourceLocation? Location { get; init; }
}

/// <summary>An authored design token (an object containing <c>$value</c>).</summary>
public sealed class TokenDefinition : TokenNode
{
    public TokenDefinition(string name, TokenPath path, SourceNode value) : base(name, path) => Value = value;

    /// <summary>The explicit <c>$type</c>, if declared on the token itself.</summary>
    public string? Type { get; init; }

    /// <summary>The raw <c>$value</c> (may contain references at any depth).</summary>
    public SourceNode Value { get; }

    /// <summary>
    /// True when the token was written as <c>{"$ref": "#/..."}</c> at token level (DTCG §7.1.2 example 32)
    /// instead of inside <c>$value</c>. Preserved for round-tripping.
    /// </summary>
    public bool IsTokenLevelPointer { get; init; }
}

/// <summary>An authored group (an object without <c>$value</c>).</summary>
public sealed class TokenGroup : TokenNode
{
    private readonly List<TokenNode> _children = [];
    private readonly Dictionary<string, TokenNode> _byName = new(StringComparer.Ordinal);

    public TokenGroup(string name, TokenPath path) : base(name, path) { }

    /// <summary>Default type for tokens in this group that do not declare their own.</summary>
    public string? Type { get; init; }

    /// <summary>Raw <c>$extends</c> reference (curly-brace or JSON Pointer form).</summary>
    public string? Extends { get; init; }

    public SourceLocation? ExtendsLocation { get; init; }

    public IReadOnlyList<TokenNode> Children => _children;

    public TokenNode? this[string name] => _byName.GetValueOrDefault(name);

    internal void AddChild(TokenNode child)
    {
        if (_byName.TryGetValue(child.Name, out var existing)) _children.Remove(existing);
        _byName[child.Name] = child;
        _children.Add(child);
    }

    /// <summary>Enumerates all descendant tokens in document order.</summary>
    public IEnumerable<TokenDefinition> EnumerateTokens()
    {
        foreach (var child in _children)
        {
            if (child is TokenDefinition t) yield return t;
            else if (child is TokenGroup g)
            {
                foreach (var nested in g.EnumerateTokens()) yield return nested;
            }
        }
    }

    public TokenNode? Find(TokenPath path)
    {
        TokenNode current = this;
        foreach (var segment in path.Segments)
        {
            if (current is not TokenGroup g || g[segment] is not { } next) return null;
            current = next;
        }
        return current;
    }
}

/// <summary>A parsed DTCG document: the root group plus the diagnostics produced while reading it.</summary>
public sealed class TokenDocument
{
    public TokenDocument(TokenGroup root, SourceObject? source, IReadOnlyList<Diagnostic> diagnostics, string? sourceName)
    {
        Root = root;
        Source = source;
        Diagnostics = diagnostics;
        SourceName = sourceName;
    }

    public TokenGroup Root { get; }
    /// <summary>The JSON the document was read from (null for programmatically built documents).</summary>
    public SourceObject? Source { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public string? SourceName { get; }
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}
