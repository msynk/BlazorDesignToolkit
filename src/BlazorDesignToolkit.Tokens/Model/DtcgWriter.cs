using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens;

public enum TokenOrdering
{
    /// <summary>Keep the order in which tokens were authored (stable for identical input).</summary>
    Document,
    /// <summary>Sort by path using ordinal comparison.</summary>
    Alphabetical,
}

public sealed record DtcgWriteOptions
{
    public static DtcgWriteOptions Default { get; } = new();

    /// <summary>Write the authored value (keeping references) instead of the resolved value.</summary>
    public bool PreserveReferences { get; init; }
    public TokenOrdering Ordering { get; init; } = TokenOrdering.Document;
    public bool Indented { get; init; } = true;
    public bool IncludeDescriptions { get; init; } = true;
    public bool IncludeExtensions { get; init; } = true;
    public bool IncludeDeprecated { get; init; } = true;
}

/// <summary>Writes DTCG 2025.10 JSON. Output is deterministic: LF newlines, two-space indentation, fixed property order.</summary>
public static class DtcgWriter
{
    /// <summary>
    /// Serializes an authored group back to DTCG JSON. Reserved properties are written first in a fixed order
    /// ($type, $description, $deprecated, $extends, $extensions, unknown), followed by children in document order.
    /// Unknown reserved properties and extension data are preserved.
    /// </summary>
    public static SourceObject ToSourceObject(TokenGroup group)
    {
        var props = new List<SourceProperty>();
        if (group.Type is not null) props.Add(new("$type", new SourceString(group.Type)));
        if (group.Description is not null) props.Add(new("$description", new SourceString(group.Description)));
        if (group.Deprecation is not null) props.Add(new("$deprecated", Deprecation(group.Deprecation)));
        if (group.Extends is not null) props.Add(new("$extends", new SourceString(group.Extends)));
        if (group.Extensions is not null) props.Add(new("$extensions", group.Extensions));
        props.AddRange(group.UnknownReservedProperties);
        foreach (var child in group.Children)
        {
            props.Add(new(child.Name, child switch
            {
                TokenDefinition t => ToSourceObject(t),
                TokenGroup g => ToSourceObject(g),
                _ => throw new NotSupportedException(),
            }));
        }
        return new SourceObject(props);
    }

    public static SourceObject ToSourceObject(TokenDefinition token)
    {
        var props = new List<SourceProperty>();
        if (token.Type is not null) props.Add(new("$type", new SourceString(token.Type)));
        if (token.IsTokenLevelPointer && ReferenceSyntax.TryGetPointer(token.Value, out var pointer)) props.Add(new("$ref", new SourceString(pointer)));
        else props.Add(new("$value", token.Value));
        if (token.Description is not null) props.Add(new("$description", new SourceString(token.Description)));
        if (token.Deprecation is not null) props.Add(new("$deprecated", Deprecation(token.Deprecation)));
        if (token.Extensions is not null) props.Add(new("$extensions", token.Extensions));
        props.AddRange(token.UnknownReservedProperties);
        return new SourceObject(props);
    }

    public static string Write(TokenDocument document, bool indented = true)
        => SourceJsonWriter.ToJsonString(document.Source ?? ToSourceObject(document.Root), indented) + (indented ? "\n" : "");

    /// <summary>
    /// Writes a resolved token set as a self-contained DTCG document: every token carries an explicit <c>$type</c>,
    /// and (unless <see cref="DtcgWriteOptions.PreserveReferences"/>) a fully resolved <c>$value</c>. Invalid tokens are omitted.
    /// </summary>
    public static SourceObject ToSourceObject(ResolvedTokenSet set, DtcgWriteOptions? options = null)
    {
        options ??= DtcgWriteOptions.Default;
        var root = new Node();
        IEnumerable<ResolvedToken> tokens = set.Tokens.Where(static t => t.IsValid);
        if (options.Ordering == TokenOrdering.Alphabetical) tokens = tokens.OrderBy(static t => t.Path);
        foreach (var token in tokens)
        {
            var node = root;
            foreach (var segment in token.Path.Segments) node = node.Child(segment);
            var props = new List<SourceProperty> { new("$type", new SourceString(token.Type!)) };
            props.Add(new("$value", options.PreserveReferences ? token.RawValue : token.Value!.ToSourceNode()));
            if (options.IncludeDescriptions && token.Description is not null) props.Add(new("$description", new SourceString(token.Description)));
            if (options.IncludeDeprecated && token.Definition.Deprecation is not null) props.Add(new("$deprecated", Deprecation(token.Definition.Deprecation)));
            if (options.IncludeExtensions && token.Extensions is not null) props.Add(new("$extensions", token.Extensions));
            node.Token = props;
        }
        return root.Build();
    }

    public static string Write(ResolvedTokenSet set, DtcgWriteOptions? options = null)
    {
        options ??= DtcgWriteOptions.Default;
        return SourceJsonWriter.ToJsonString(ToSourceObject(set, options), options.Indented) + (options.Indented ? "\n" : "");
    }

    private static SourceNode Deprecation(TokenDeprecation d)
        => d.Explanation is not null ? new SourceString(d.Explanation) : new SourceBoolean(d.IsDeprecated);

    private sealed class Node
    {
        private readonly List<(string Name, Node Node)> _children = [];
        public List<SourceProperty>? Token { get; set; }

        public Node Child(string name)
        {
            foreach (var (n, node) in _children)
            {
                if (n == name) return node;
            }
            var created = new Node();
            _children.Add((name, created));
            return created;
        }

        public SourceObject Build()
            => Token is not null
                ? new SourceObject(Token)
                : new SourceObject(_children.Select(static c => new SourceProperty(c.Name, c.Node.Build())));
    }
}

/// <summary>
/// Merges token trees in order (later layers win). Groups merge recursively; a token at the same path replaces the
/// earlier token entirely, matching DTCG resolver-module and <c>$extends</c> semantics.
/// </summary>
public static class TokenTreeMerger
{
    public static SourceObject Merge(IEnumerable<SourceObject> layers)
    {
        SourceObject? result = null;
        foreach (var layer in layers) result = result is null ? layer : Merge(result, layer);
        return result ?? SourceObject.Empty;
    }

    public static SourceObject Merge(SourceObject lower, SourceObject upper)
    {
        var props = lower.Properties.ToList();
        foreach (var p in upper.Properties)
        {
            var index = props.FindIndex(x => x.Name == p.Name);
            if (index < 0)
            {
                props.Add(p);
                continue;
            }
            var existing = props[index].Value;
            if (!p.Name.StartsWith('$') && existing is SourceObject eo && p.Value is SourceObject uo && !DtcgReader.IsTokenObject(eo) && !DtcgReader.IsTokenObject(uo))
            {
                props[index] = p with { Value = Merge(eo, uo) };
            }
            else
            {
                props[index] = p;
            }
        }
        return new SourceObject(props, lower.Location);
    }

    /// <summary>Lists the token paths defined in a raw tree (without resolving anything).</summary>
    public static IEnumerable<(TokenPath Path, SourceObject Token)> EnumerateTokens(SourceObject tree, TokenPath? prefix = null)
    {
        var basePath = prefix ?? TokenPath.Empty;
        foreach (var p in tree.Properties)
        {
            if ((p.Name.StartsWith('$') && p.Name != TokenPath.RootTokenName) || p.Value is not SourceObject child) continue;
            var path = basePath.Append(p.Name);
            if (DtcgReader.IsTokenObject(child)) yield return (path, child);
            else
            {
                foreach (var nested in EnumerateTokens(child, path)) yield return nested;
            }
        }
    }
}
