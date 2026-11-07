using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens.Analysis;

public enum TokenChangeKind
{
    Added,
    Removed,
    TypeChanged,
    ValueChanged,
    ReferenceChanged,
    Deprecated,
    Undeprecated,
    DescriptionChanged,
}

public sealed record TokenChange(string Path, TokenChangeKind Kind, string? Before, string? After)
{
    /// <summary>Removals and type changes break consumers.</summary>
    public bool IsBreaking => Kind is TokenChangeKind.Removed or TokenChangeKind.TypeChanged;
}

public sealed class TokenDiffResult(IReadOnlyList<TokenChange> changes)
{
    public IReadOnlyList<TokenChange> Changes { get; } = changes;
    public bool HasChanges => Changes.Count > 0;
    public bool HasBreakingChanges => Changes.Any(static c => c.IsBreaking);
    public int Count(TokenChangeKind kind) => Changes.Count(c => c.Kind == kind);
}

/// <summary>Compares two resolved token sets (for example two versions of a design system, or two themes).</summary>
public static class TokenDiff
{
    public static TokenDiffResult Compare(ResolvedTokenSet before, ResolvedTokenSet after)
    {
        var changes = new List<TokenChange>();
        var paths = before.Tokens.Select(static t => t.Path).Union(after.Tokens.Select(static t => t.Path)).Order();
        foreach (var path in paths)
        {
            var a = before[path];
            var b = after[path];
            var p = path.ToString();
            if (a is null) { changes.Add(new(p, TokenChangeKind.Added, null, Describe(b!))); continue; }
            if (b is null) { changes.Add(new(p, TokenChangeKind.Removed, Describe(a), null)); continue; }
            if (a.Type != b.Type) { changes.Add(new(p, TokenChangeKind.TypeChanged, a.Type, b.Type)); continue; }
            var va = a.Value?.ToSourceNode().ToJson() ?? a.RawValue.ToJson();
            var vb = b.Value?.ToSourceNode().ToJson() ?? b.RawValue.ToJson();
            if (va != vb) changes.Add(new(p, TokenChangeKind.ValueChanged, va, vb));
            if (a.AliasOf != b.AliasOf) changes.Add(new(p, TokenChangeKind.ReferenceChanged, a.AliasOf?.ToString(), b.AliasOf?.ToString()));
            if (!a.IsDeprecated && b.IsDeprecated) changes.Add(new(p, TokenChangeKind.Deprecated, null, b.Deprecation!.Explanation));
            if (a.IsDeprecated && !b.IsDeprecated) changes.Add(new(p, TokenChangeKind.Undeprecated, a.Deprecation!.Explanation, null));
            if (a.Description != b.Description) changes.Add(new(p, TokenChangeKind.DescriptionChanged, a.Description, b.Description));
        }
        return new TokenDiffResult(changes);
    }

    private static string Describe(ResolvedToken t) => $"{t.Type}: {t.Value?.ToSourceNode().ToJson() ?? t.RawValue.ToJson()}";
}
