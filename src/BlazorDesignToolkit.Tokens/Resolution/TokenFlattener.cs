using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Resolution;

/// <summary>A token after group extension (<c>$extends</c>) and group-property inheritance, before reference resolution.</summary>
public sealed class FlatToken
{
    internal FlatToken(TokenPath path, TokenDefinition definition, string? groupType, TokenDeprecation? deprecation, TokenPath? inheritedFrom)
    {
        Path = path;
        Definition = definition;
        GroupType = groupType;
        Deprecation = deprecation;
        InheritedFrom = inheritedFrom;
    }

    public TokenPath Path { get; }
    public TokenDefinition Definition { get; }
    /// <summary>The nearest group <c>$type</c> (after extension resolution), used when the token has no explicit type and is not an alias.</summary>
    public string? GroupType { get; }
    /// <summary>Effective deprecation: the token's own, otherwise the nearest group's.</summary>
    public TokenDeprecation? Deprecation { get; }
    /// <summary>Original path when the token was inherited through <c>$extends</c>.</summary>
    public TokenPath? InheritedFrom { get; }
}

/// <summary>The flattened token set: tokens in document order plus the set of group paths.</summary>
public sealed class FlatTokenSet
{
    private readonly Dictionary<TokenPath, FlatToken> _tokens;

    internal FlatTokenSet(List<FlatToken> tokens, HashSet<TokenPath> groups, IReadOnlyList<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        _tokens = new Dictionary<TokenPath, FlatToken>(tokens.Count);
        foreach (var t in tokens) _tokens[t.Path] = t;
        Groups = groups;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<FlatToken> Tokens { get; }
    public IReadOnlySet<TokenPath> Groups { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public FlatToken? this[TokenPath path] => _tokens.GetValueOrDefault(path);

    public bool ContainsToken(TokenPath path) => _tokens.ContainsKey(path);

    public IEnumerable<TokenPath> Paths => _tokens.Keys;
}

/// <summary>
/// Applies DTCG 2025.10 group semantics: <c>$extends</c> deep-merge (groups merge recursively, tokens are replaced whole),
/// circular-extension detection, and inheritance of <c>$type</c> and <c>$deprecated</c> from groups.
/// </summary>
public static class TokenFlattener
{
    public static FlatTokenSet Flatten(TokenDocument document) => Flatten(document.Root);

    public static FlatTokenSet Flatten(TokenGroup root)
    {
        // A group may extend a group that exists only through another extension. Such targets are not in the source tree,
        // so they are looked up in the result of the previous pass; passes repeat until the result is stable. Documents
        // without such targets need a single pass. A chain that keeps growing is circular.
        var state = new State(root, null);
        var effective = state.Compute(root);
        var signature = state.HasMissingTargets ? Signature(effective) : "";
        var maxPasses = state.HasMissingTargets ? 2 + CountExtends(root) : 0;
        for (var pass = 1; state.HasMissingTargets && pass < maxPasses; pass++)
        {
            var next = new State(root, effective);
            var nextEffective = next.Compute(root);
            var nextSignature = Signature(nextEffective);
            var stable = nextSignature == signature;
            state = next;
            effective = nextEffective;
            signature = nextSignature;
            if (stable) break;
            if (pass == maxPasses - 1) state.ReportUnstable();
        }
        var tokens = new List<FlatToken>();
        var groups = new HashSet<TokenPath>();
        Collect(effective, null, null, tokens, groups);
        return new FlatTokenSet(tokens, groups, state.Bag.ToSortedList());
    }

    private static int CountExtends(TokenGroup group)
        => (group.Extends is null ? 0 : 1) + group.Children.OfType<TokenGroup>().Sum(CountExtends);

    private static string Signature(EffectiveGroup group)
    {
        var sb = new System.Text.StringBuilder();
        Append(group);
        return sb.ToString();

        void Append(EffectiveNode node)
        {
            sb.Append(node.Path.ToString()).Append(node is EffectiveToken ? ':' : '/').Append('|');
            if (node is EffectiveGroup g) foreach (var child in g.Children) Append(child);
        }
    }

    private static void Collect(EffectiveGroup group, string? inheritedType, TokenDeprecation? inheritedDeprecation, List<FlatToken> tokens, HashSet<TokenPath> groups)
    {
        var type = group.Type ?? inheritedType;
        var deprecation = group.Deprecation ?? inheritedDeprecation;
        if (!group.Path.IsEmpty) groups.Add(group.Path);
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case EffectiveToken t:
                    tokens.Add(new FlatToken(t.Path, t.Definition, type, t.Definition.Deprecation ?? deprecation, t.InheritedFrom));
                    break;
                case EffectiveGroup g:
                    Collect(g, type, deprecation, tokens, groups);
                    break;
            }
        }
    }

    private abstract class EffectiveNode(TokenPath path)
    {
        public TokenPath Path { get; } = path;
        public string Name => Path.Name;
        public abstract EffectiveNode Repath(TokenPath newPath);
    }

    private sealed class EffectiveToken(TokenPath path, TokenDefinition definition, TokenPath? inheritedFrom) : EffectiveNode(path)
    {
        public TokenDefinition Definition { get; } = definition;
        public TokenPath? InheritedFrom { get; } = inheritedFrom;
        public override EffectiveNode Repath(TokenPath newPath) => new EffectiveToken(newPath, Definition, InheritedFrom ?? Path);
    }

    private sealed class EffectiveGroup(TokenPath path, string? type, TokenDeprecation? deprecation, List<EffectiveNode> children) : EffectiveNode(path)
    {
        public string? Type { get; } = type;
        public TokenDeprecation? Deprecation { get; } = deprecation;
        public List<EffectiveNode> Children { get; } = children;

        public EffectiveNode? Find(string name) => Children.Find(c => c.Name == name);

        public override EffectiveNode Repath(TokenPath newPath)
            => new EffectiveGroup(newPath, Type, Deprecation, Children.Select(c => c.Repath(newPath.Append(c.Name))).ToList());
    }

    private sealed class State(TokenGroup root, EffectiveGroup? previous)
    {
        private readonly List<(TokenGroup Group, TokenPath Target)> _resolvedFromPrevious = [];

        /// <summary>True when an $extends target was not found in the source tree (it may exist only through extension).</summary>
        public bool HasMissingTargets { get; private set; }

        /// <summary>Reports the groups whose inherited targets did not stabilize as circular.</summary>
        public void ReportUnstable()
        {
            foreach (var (group, target) in _resolvedFromPrevious)
            {
                Report(DiagnosticCatalog.CircularExtends, group,
                    $"$extends of '{group.Path}' references '{target}', which only exists through a chain of extensions that does not terminate.",
                    group.Extends ?? "", [group.Path.ToString(), target.ToString()]);
            }
        }

        private readonly Dictionary<TokenGroup, EffectiveGroup> _memo = new(ReferenceEqualityComparer.Instance);
        private readonly List<TokenGroup> _inProgress = [];
        private readonly HashSet<string> _reportedCycles = new(StringComparer.Ordinal);
        public DiagnosticBag Bag { get; } = new();

        public EffectiveGroup Compute(TokenGroup group)
        {
            if (_memo.TryGetValue(group, out var done)) return done;
            _inProgress.Add(group);

            EffectiveGroup? baseGroup = null;
            if (group.Extends is not null) baseGroup = ResolveExtends(group);

            var local = new List<EffectiveNode>();
            foreach (var child in group.Children)
            {
                if (child is TokenDefinition t) local.Add(new EffectiveToken(t.Path, t, null));
                else if (child is TokenGroup g)
                {
                    if (_inProgress.Contains(g))
                    {
                        ReportCycle(g);
                        continue;
                    }
                    local.Add(Compute(g));
                }
            }

            EffectiveGroup result;
            if (baseGroup is null)
            {
                result = new EffectiveGroup(group.Path, group.Type, group.Deprecation, local);
            }
            else
            {
                var rebased = (EffectiveGroup)baseGroup.Repath(group.Path);
                result = new EffectiveGroup(group.Path, group.Type ?? rebased.Type, group.Deprecation ?? rebased.Deprecation, Merge(rebased.Children, local));
            }

            _inProgress.Remove(group);
            _memo[group] = result;
            return result;
        }

        private static List<EffectiveNode> Merge(List<EffectiveNode> inherited, List<EffectiveNode> local)
        {
            var result = new List<EffectiveNode>(inherited);
            foreach (var node in local)
            {
                var index = result.FindIndex(n => n.Name == node.Name);
                if (index < 0)
                {
                    result.Add(node);
                }
                else if (result[index] is EffectiveGroup baseGroup && node is EffectiveGroup localGroup)
                {
                    // Same path, both groups: merge recursively (DTCG §6.4.3 "different paths = merge").
                    result[index] = new EffectiveGroup(localGroup.Path, localGroup.Type ?? baseGroup.Type, localGroup.Deprecation ?? baseGroup.Deprecation,
                        Merge(baseGroup.Children, localGroup.Children));
                }
                else
                {
                    // Same path: the local definition replaces the inherited one entirely (tokens are never merged property-by-property).
                    result[index] = node;
                }
            }
            return result;
        }

        private EffectiveGroup? ResolveExtends(TokenGroup group)
        {
            var raw = group.Extends!;
            TokenPath target;
            if (ReferenceSyntax.TryParseAlias(raw, out var aliasPath))
            {
                target = aliasPath;
            }
            else if (raw.StartsWith('#'))
            {
                if (!JsonPointer.TryParse(raw, out var segments, out var error))
                {
                    Report(DiagnosticCatalog.InvalidExtends, group, $"$extends of '{group.Path}' is not a valid JSON Pointer: {error}", raw);
                    return null;
                }
                if (segments.Contains("$value"))
                {
                    Report(DiagnosticCatalog.InvalidExtends, group, $"$extends of '{group.Path}' must reference a group, not a token value.", raw);
                    return null;
                }
                target = new TokenPath(segments);
            }
            else
            {
                Report(DiagnosticCatalog.InvalidExtends, group,
                    $"$extends of '{group.Path}' must be a curly-brace reference such as '{{button}}' or a JSON Pointer such as '#/button'.", raw);
                return null;
            }

            if (target == group.Path)
            {
                Report(DiagnosticCatalog.CircularExtends, group, $"Group '{group.Path}' extends itself.", raw, [group.Path.ToString()]);
                return null;
            }
            if (group.Path.StartsWith(target))
            {
                Report(DiagnosticCatalog.CircularExtends, group,
                    $"Group '{group.Path}' extends its ancestor '{target}', which contains it; this creates circular inheritance.", raw,
                    [group.Path.ToString(), target.ToString()]);
                return null;
            }

            var node = root.Find(target);
            switch (node)
            {
                case TokenGroup targetGroup:
                    if (_inProgress.Contains(targetGroup))
                    {
                        var chain = _inProgress.SkipWhile(g => g != targetGroup).Select(g => g.Path.ToString()).Append(targetGroup.Path.ToString()).ToList();
                        var key = string.Join(">", chain.Order(StringComparer.Ordinal));
                        if (_reportedCycles.Add(key))
                        {
                            Report(DiagnosticCatalog.CircularExtends, group, $"Circular $extends chain: {string.Join(" -> ", chain)}.", raw, chain);
                        }
                        return null;
                    }
                    return Compute(targetGroup);
                case TokenDefinition:
                    Report(DiagnosticCatalog.InvalidExtends, group, $"$extends of '{group.Path}' references the token '{target}'; $extends must reference a group.", raw);
                    return null;
                default:
                    HasMissingTargets = true;
                    if (DependsOn(target, group.Path))
                    {
                        Report(DiagnosticCatalog.CircularExtends, group,
                            $"$extends of '{group.Path}' references '{target}', which only exists through an extension that depends on '{group.Path}'.",
                            raw, [group.Path.ToString(), target.ToString()]);
                        return null;
                    }
                    switch (previous is null ? null : FindEffective(previous, target))
                    {
                        case EffectiveGroup inherited:
                            _resolvedFromPrevious.Add((group, target));
                            return inherited;
                        case EffectiveToken:
                            Report(DiagnosticCatalog.InvalidExtends, group, $"$extends of '{group.Path}' references the token '{target}'; $extends must reference a group.", raw);
                            return null;
                    }
                    Report(DiagnosticCatalog.InvalidExtends, group, $"$extends of '{group.Path}' references '{target}', which does not exist.", raw);
                    return null;
            }
        }

        /// <summary>
        /// True when the groups that could provide <paramref name="target"/> (its ancestors in the source tree and, transitively,
        /// the groups they extend) include <paramref name="group"/>, one of its ancestors or one of its descendants.
        /// </summary>
        private bool DependsOn(TokenPath target, TokenPath group)
        {
            var queue = new Queue<TokenPath>([target]);
            var seen = new HashSet<TokenPath>();
            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                if (!seen.Add(path)) continue;
                for (var length = path.Length; length >= 1; length--)
                {
                    var prefix = new TokenPath(path.Segments.Take(length));
                    if (root.Find(prefix) is not TokenGroup { Extends: { } extends }) continue;
                    if (!TryParseTarget(extends, out var next)) continue;
                    if (next.StartsWith(group) || group.StartsWith(next)) return true;
                    // What the extension provides below the prefix is looked up in the extended group.
                    queue.Enqueue(new TokenPath(next.Segments.Concat(path.Segments.Skip(length))));
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private static bool TryParseTarget(string raw, out TokenPath target)
        {
            if (ReferenceSyntax.TryParseAlias(raw, out target)) return true;
            if (raw.StartsWith('#') && JsonPointer.TryParse(raw, out var segments, out _) && !segments.Contains("$value"))
            {
                target = new TokenPath(segments);
                return true;
            }
            target = default;
            return false;
        }

        private static EffectiveNode? FindEffective(EffectiveGroup root, TokenPath path)
        {
            EffectiveNode current = root;
            foreach (var segment in path.Segments)
            {
                if (current is not EffectiveGroup g || g.Find(segment) is not { } child) return null;
                current = child;
            }
            return current;
        }

        private void ReportCycle(TokenGroup group)
        {
            var chain = _inProgress.SkipWhile(g => g != group).Select(g => g.Path.ToString()).Append(group.Path.ToString()).ToList();
            var key = string.Join(">", chain.Order(StringComparer.Ordinal));
            if (_reportedCycles.Add(key))
            {
                Report(DiagnosticCatalog.CircularExtends, group, $"Circular $extends chain: {string.Join(" -> ", chain)}.", group.Extends ?? "", chain);
            }
        }

        private void Report(DiagnosticDescriptor descriptor, TokenGroup group, string message, string actual, IReadOnlyList<string>? chain = null)
            => Bag.Report(descriptor, message, group.Path.ToString(), group.ExtendsLocation ?? group.Location, actual: actual, referenceChain: chain);
    }
}
