using System.Collections.Immutable;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Theming;

public sealed class ThemeManifestResult(ThemeCatalog? catalog, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<string> files)
{
    /// <summary>The catalog, or null when the manifest has errors that prevent building one.</summary>
    public ThemeCatalog? Catalog { get; } = catalog;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    /// <summary>Every token file the manifest pulled in (for watch mode and coverage).</summary>
    public IReadOnlyList<string> Files { get; } = files;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Reads a theme manifest (<c>*.themes.json</c>): axes, themes, layers with conditions, and themes defined by DTCG resolver documents.
/// File references are resolved relative to the manifest and may not leave its directory.
/// </summary>
/// <example>
/// <code>
/// {
///   "defaultTheme": "aurora",
///   "axes": [ { "name": "mode", "values": ["light", "dark"], "default": "light", "system": { "dark": "(prefers-color-scheme: dark)" } } ],
///   "themes": [
///     { "id": "aurora", "name": "Aurora", "layers": [ { "source": "foundation.tokens.json" }, { "source": "dark.tokens.json", "when": { "mode": "dark" } } ] },
///     { "id": "spec", "resolver": "aurora.resolver.json" }
///   ]
/// }
/// </code>
/// </example>
public static class ThemeManifest
{
    public static async Task<ThemeManifestResult> LoadAsync(string path, JsonParseOptions? parseOptions = null, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var parsed = await SourceJsonParser.ParseFileAsync(full, parseOptions, cancellationToken).ConfigureAwait(false);
        if (parsed.Root is not SourceObject root)
        {
            var bag = new DiagnosticBag();
            bag.AddRange(parsed.Diagnostics);
            if (parsed.Root is not null) bag.Report(DiagnosticCatalog.InvalidConfiguration, "A theme manifest must be a JSON object.", location: parsed.Root.Location);
            return new ThemeManifestResult(null, bag.ToSortedList(), []);
        }
        var directory = Path.GetDirectoryName(full)!;
        return await LoadAsync(root, directory, new FileSystemResolverSourceLoader(directory, parseOptions), parsed.Diagnostics, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ThemeManifestResult> LoadAsync(SourceObject root, string? baseDirectory, IResolverSourceLoader loader,
        IEnumerable<Diagnostic>? prior = null, CancellationToken cancellationToken = default)
    {
        var bag = new DiagnosticBag();
        if (prior is not null) bag.AddRange(prior);
        var files = new List<string>();
        var builder = new ThemeCatalogBuilder();

        if (root["axes"] is SourceArray axesNode)
        {
            foreach (var item in axesNode.Items)
            {
                if (ReadAxis(item, bag) is { } axis) builder.AddAxis(axis);
            }
        }
        else if (root["axes"] is { } badAxes)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "\"axes\" must be an array.", location: badAxes.Location);
        }

        var declaredAxes = new Dictionary<string, ThemeAxisDefinition>(StringComparer.Ordinal);
        if (root["axes"] is SourceArray a2)
        {
            foreach (var item in a2.Items) if (ReadAxis(item, new DiagnosticBag()) is { } ax) declaredAxes[ax.Name] = ax;
        }

        if (root["themes"] is not SourceArray themesNode || themesNode.Items.Count == 0)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "A theme manifest needs a non-empty \"themes\" array.", location: root.Location);
            return new ThemeManifestResult(null, bag.ToSortedList(), files);
        }

        var cache = new Dictionary<string, SourceObject?>(StringComparer.Ordinal);
        async Task<SourceObject?> Load(string reference, SourceLocation? location)
        {
            if (cache.TryGetValue(reference, out var cached)) return cached;
            var result = await loader.LoadAsync(reference, baseDirectory, cancellationToken).ConfigureAwait(false);
            foreach (var d in result.Diagnostics) bag.Add(d.Location is null && location is not null ? Relocate(d, location) : d);
            var obj = result.Root as SourceObject;
            if (result.Root is not null && obj is null) bag.Report(DiagnosticCatalog.InvalidConfiguration, $"'{reference}' must contain a JSON object.", location: location);
            if (obj is not null) files.Add(baseDirectory is null ? reference : Path.GetFullPath(Path.Combine(baseDirectory, reference)));
            cache[reference] = obj;
            return obj;
        }

        foreach (var item in themesNode.Items)
        {
            if (item is not SourceObject themeObj || themeObj["id"] is not SourceString { Value: var id })
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, "Each theme needs a string \"id\".", location: item.Location);
                continue;
            }
            if (!ThemeIdentifiers.IsValid(id))
            {
                bag.Report(DiagnosticCatalog.InvalidIdentifier, $"Invalid theme id '{id}'. {ThemeIdentifiers.Rule}", location: themeObj["id"]!.Location, actual: id);
                continue;
            }
            var layers = new List<ThemeLayer>();
            if (themeObj["resolver"] is SourceString { Value: var resolverPath } resolverNode)
            {
                var resolverRoot = await Load(resolverPath, resolverNode.Location).ConfigureAwait(false);
                if (resolverRoot is null) continue;
                var resolverDirectory = baseDirectory is null ? null : Path.GetDirectoryName(Path.GetFullPath(Path.Combine(baseDirectory, resolverPath)));
                var document = DtcgResolver.Read(resolverRoot, resolverPath, resolverDirectory);
                bag.AddRange(document.Diagnostics);
                if (document.HasErrors) continue;
                var (resolverLayers, resolverAxes) = await ThemeDefinitions.FromResolverAsync(document, loader, bag, cancellationToken).ConfigureAwait(false);
                layers.AddRange(resolverLayers);
                foreach (var axis in resolverAxes)
                {
                    if (declaredAxes.TryGetValue(axis.Name, out var existing))
                    {
                        if (!existing.Values.SequenceEqual(axis.Values))
                        {
                            bag.Report(DiagnosticCatalog.InvalidConfiguration,
                                $"Resolver modifier '{axis.Name}' has contexts [{string.Join(", ", axis.Values)}] but the manifest axis declares [{string.Join(", ", existing.Values)}].",
                                location: resolverNode.Location);
                        }
                        continue;
                    }
                    declaredAxes[axis.Name] = axis;
                    builder.AddAxis(axis);
                }
            }
            if (themeObj["layers"] is SourceArray layersNode)
            {
                foreach (var layerNode in layersNode.Items)
                {
                    if (layerNode is not SourceObject layerObj)
                    {
                        bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Layers of theme '{id}' must be objects.", location: layerNode.Location);
                        continue;
                    }
                    SourceObject? tokens = null;
                    var name = (layerObj["source"] as SourceString)?.Value;
                    if (name is not null) tokens = await Load(name, layerObj["source"]!.Location).ConfigureAwait(false);
                    else if (layerObj["tokens"] is SourceObject inline) { tokens = inline; name = $"{id} (inline layer)"; }
                    else bag.Report(DiagnosticCatalog.InvalidConfiguration, $"A layer of theme '{id}' needs \"source\" or inline \"tokens\".", location: layerObj.Location);
                    if (tokens is null) continue;
                    var when = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (layerObj["when"] is SourceObject whenObj)
                    {
                        foreach (var p in whenObj.Properties)
                        {
                            if (p.Value is SourceString s) when[p.Name] = s.Value;
                            else bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Condition '{p.Name}' of a layer of theme '{id}' must be a string.", location: p.Value.Location);
                        }
                    }
                    layers.Add(new ThemeLayer(tokens, name, when) { MayAddTokens = (layerObj["mayAddTokens"] as SourceBoolean)?.Value });
                }
            }
            if (layers.Count == 0)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Theme '{id}' has no layers (declare \"layers\" or \"resolver\").", location: themeObj.Location);
                continue;
            }
            var metadata = themeObj["metadata"] is SourceObject md
                ? md.Properties.Where(static p => p.Value is SourceString).ToImmutableDictionary(static p => p.Name, static p => ((SourceString)p.Value).Value, StringComparer.Ordinal)
                : ImmutableDictionary<string, string>.Empty;
            builder.AddTheme(new ThemeDefinition(id, layers)
            {
                DisplayName = (themeObj["name"] as SourceString)?.Value,
                Description = (themeObj["description"] as SourceString)?.Value,
                Extends = (themeObj["extends"] as SourceString)?.Value,
                Metadata = metadata,
            });
        }

        if (root["defaultTheme"] is SourceString def) builder.SetDefaultTheme(def.Value);
        if (bag.HasErrors) return new ThemeManifestResult(null, bag.ToSortedList(), files);
        try
        {
            var catalog = builder.Build();
            bag.AddRange(catalog.Diagnostics);
            return new ThemeManifestResult(catalog.HasErrors ? null : catalog, bag.ToSortedList(), files);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, ex.Message);
            return new ThemeManifestResult(null, bag.ToSortedList(), files);
        }
    }

    private static Diagnostic Relocate(Diagnostic d, SourceLocation location)
        => new(d.RuleId, d.Severity, d.Message, d.TokenPath, location, d.Expected, d.Actual, d.Remediation, d.ReferenceChain, d.Component, d.Category);

    private static ThemeAxisDefinition? ReadAxis(SourceNode node, DiagnosticBag bag)
    {
        if (node is not SourceObject o || o["name"] is not SourceString { Value: var name } || o["values"] is not SourceArray values)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "Each axis needs a \"name\" and a \"values\" array.", location: node.Location);
            return null;
        }
        var list = values.Items.OfType<SourceString>().Select(static s => s.Value).ToList();
        var def = (o["default"] as SourceString)?.Value ?? list.FirstOrDefault() ?? "";
        try
        {
            var system = o["system"] is SourceObject sys
                ? sys.Properties.Where(static p => p.Value is SourceString).ToImmutableDictionary(static p => p.Name, static p => ((SourceString)p.Value).Value, StringComparer.Ordinal)
                : ImmutableDictionary<string, string>.Empty;
            return new ThemeAxisDefinition(name, list, def) { DisplayName = (o["displayName"] as SourceString)?.Value, SystemMediaQueries = system };
        }
        catch (ArgumentException ex)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, ex.Message, location: node.Location);
            return null;
        }
    }
}

/// <summary>Builds theme layers from other interoperable sources.</summary>
public static class ThemeDefinitions
{
    /// <summary>
    /// Converts a DTCG resolver document into theme layers and axes: every set becomes an unconditional layer, every modifier
    /// becomes an axis whose contexts are its values, and resolution order is preserved exactly.
    /// </summary>
    public static async Task<(IReadOnlyList<ThemeLayer> Layers, IReadOnlyList<ThemeAxisDefinition> Axes)> FromResolverAsync(
        ResolverDocument document, IResolverSourceLoader loader, DiagnosticBag bag, CancellationToken cancellationToken = default)
    {
        var session = new ResolverSession(document, loader);
        var layers = new List<ThemeLayer>();
        var axes = new List<ThemeAxisDefinition>();
        foreach (var item in document.ResolutionOrder)
        {
            if (item.Set is not null)
            {
                foreach (var tree in await session.GetSetLayersAsync(item.Set, bag, cancellationToken).ConfigureAwait(false))
                {
                    layers.Add(new ThemeLayer(tree, $"set:{item.Set.Name}"));
                }
                continue;
            }
            var modifier = item.Modifier!;
            var names = modifier.ContextNames.ToList();
            if (!ThemeIdentifiers.IsValid(modifier.Name) || names.Any(n => !ThemeIdentifiers.IsValid(n) || n == ThemeAxes.System))
            {
                bag.Report(DiagnosticCatalog.InvalidIdentifier,
                    $"Modifier '{modifier.Name}' or one of its contexts is not a valid theme axis identifier ({ThemeIdentifiers.Rule}).", actual: modifier.Name);
                continue;
            }
            axes.Add(new ThemeAxisDefinition(modifier.Name, names, modifier.Default ?? names[0]) { DisplayName = modifier.Description });
            foreach (var context in names)
            {
                foreach (var tree in await session.GetContextLayersAsync(modifier, context, bag, cancellationToken).ConfigureAwait(false))
                {
                    // Contexts of one modifier are alternatives, so each may define tokens the others do not.
                    layers.Add(new ThemeLayer(tree, $"modifier:{modifier.Name}={context}", new Dictionary<string, string> { [modifier.Name] = context }) { MayAddTokens = true });
                }
            }
        }
        return (layers, axes);
    }
}
