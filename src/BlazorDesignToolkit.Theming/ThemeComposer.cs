using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Theming;

/// <summary>
/// Composes a theme: layers of the inheritance chain (ancestors first) whose conditions match the selection are merged
/// in order, optional runtime overrides are applied last, and references are resolved only after merging so that
/// semantic aliases pick up overridden primitives. Composition is deterministic.
/// </summary>
public static class ThemeComposer
{
    public static ThemeResolution Compose(ThemeCatalog catalog, ThemeSelection selection, SourceObject? overrides = null,
        IEnumerable<Diagnostic>? prior = null, CancellationToken cancellationToken = default)
    {
        var bag = new DiagnosticBag();
        if (prior is not null) bag.AddRange(prior);
        var chain = catalog.GetInheritanceChain(selection.ThemeId);
        var merged = SourceObject.Empty;
        var known = new Dictionary<TokenPath, string?>();
        foreach (var theme in chain)
        {
            foreach (var layer in theme.Layers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!layer.Matches(selection.Axes)) continue;
                // Unconditional layers build the base and may add tokens; conditional (variant) layers are expected to override.
                CheckLayer(theme, layer, known, layer.MayAddTokens ?? layer.When.IsEmpty, bag);
                merged = TokenTreeMerger.Merge(merged, layer.Tokens);
            }
        }
        if (overrides is not null)
        {
            CheckLayer(null, new ThemeLayer(overrides, "runtime overrides"), known, mayAdd: false, bag);
            merged = TokenTreeMerger.Merge(merged, overrides);
        }
        var document = DtcgReader.Read(merged, $"theme:{selection.ThemeId}");
        var resolved = TokenResolver.Resolve(document, catalog.ResolutionOptions, cancellationToken);
        bag.AddRange(resolved.Diagnostics);
        var scope = Scope(selection);
        var diagnostics = bag.ToSortedList().Select(d => d.Scope is null ? d.WithScope(scope) : d).ToList();
        return new ThemeResolution(selection, resolved, diagnostics, merged);
    }

    public static string Scope(ThemeSelection selection)
        => $"theme={selection.ThemeId}" + string.Concat(selection.Axes.Select(static a => $" {a.Key}={a.Value}"));

    /// <summary>Reports layers that add tokens unexpectedly (BDT6001) or change a token's declared type (BDT6002).</summary>
    private static void CheckLayer(ThemeDefinition? theme, ThemeLayer layer, Dictionary<TokenPath, string?> known, bool mayAdd, DiagnosticBag bag)
    {
        var origin = layer.Name ?? "(unnamed layer)";
        var owner = theme is null ? "" : $" of theme '{theme.Id}'";
        foreach (var (path, type) in EnumerateTypedTokens(layer.Tokens, TokenPath.Empty, null))
        {
            if (known.TryGetValue(path, out var existingType))
            {
                if (existingType is not null && type is not null && existingType != type)
                {
                    bag.Report(DiagnosticCatalog.OverrideTypeMismatch,
                        $"Layer '{origin}'{owner} overrides '{path}' ({existingType}) with a {type} value.", path.ToString(), expected: existingType, actual: type);
                }
            }
            else if (!mayAdd)
            {
                bag.Report(DiagnosticCatalog.OverrideAddsToken,
                    $"Layer '{origin}'{owner} defines '{path}', which no lower layer defines; switching to this variant adds a token instead of overriding one.",
                    path.ToString(), remediation: "Define the token in the base layer, fix the path, or mark the layer with MayAddTokens = true.");
            }
            known[path] = type ?? (known.TryGetValue(path, out var t) ? t : null);
        }
    }

    /// <summary>Lists tokens of a raw tree with their declared (explicit or group) type; aliases without a type yield null.</summary>
    internal static IEnumerable<(TokenPath Path, string? Type)> EnumerateTypedTokens(SourceObject tree, TokenPath prefix, string? inheritedType)
    {
        var groupType = tree["$type"] is SourceString s ? s.Value : inheritedType;
        foreach (var p in tree.Properties)
        {
            if ((p.Name.StartsWith('$') && p.Name != TokenPath.RootTokenName) || p.Value is not SourceObject child) continue;
            var path = prefix.Append(p.Name);
            if (DtcgReader.IsTokenObject(child))
            {
                var explicitType = child["$type"] is SourceString t ? t.Value : null;
                var isAlias = child["$value"] is { } v && ReferenceSyntax.IsReference(v) || child.ContainsKey("$ref");
                yield return (path, explicitType ?? (isAlias ? null : groupType));
            }
            else
            {
                foreach (var nested in EnumerateTypedTokens(child, path, groupType)) yield return nested;
            }
        }
    }
}
