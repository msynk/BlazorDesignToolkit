using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Resolution;

/// <summary>Result of resolving a resolver document for one set of inputs.</summary>
public sealed class ResolverResult(IReadOnlyDictionary<string, string> inputs, SourceObject tokens, ResolvedTokenSet resolved, IReadOnlyList<Diagnostic> diagnostics)
{
    /// <summary>Effective inputs (canonical modifier and context names, including defaults).</summary>
    public IReadOnlyDictionary<string, string> Inputs { get; } = inputs;
    /// <summary>The flattened token tree after merging every layer in resolution order.</summary>
    public SourceObject Tokens { get; } = tokens;
    public ResolvedTokenSet Resolved { get; } = resolved;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Reads and evaluates DTCG 2025.10 resolver documents (resolver module §4–§6).</summary>
public static class DtcgResolver
{
    private static readonly HashSet<string> s_rootKeys = new(StringComparer.Ordinal)
        { "name", "version", "description", "sets", "modifiers", "resolutionOrder", "$schema", "$defs", "$extensions" };

    public static async Task<ResolverDocument> ReadFileAsync(string path, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        var parsed = await SourceJsonParser.ParseFileAsync(path, options, cancellationToken).ConfigureAwait(false);
        return FromParse(parsed, path, Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    public static ResolverDocument Read(string json, string? sourceName = null, string? baseDirectory = null, JsonParseOptions? options = null)
    {
        var parsed = SourceJsonParser.Parse(json, (options ?? JsonParseOptions.Default) with { SourceName = sourceName });
        return FromParse(parsed, sourceName, baseDirectory);
    }

    private static ResolverDocument FromParse(SourceParseResult parsed, string? sourceName, string? baseDirectory)
    {
        if (parsed.Root is SourceObject root) return Read(root, sourceName, baseDirectory, parsed.Diagnostics);
        var bag = new DiagnosticBag();
        bag.AddRange(parsed.Diagnostics);
        if (parsed.Root is not null) bag.Report(DiagnosticCatalog.InvalidResolverDocument, "A resolver document must be a JSON object.", location: parsed.Root.Location);
        return new ResolverDocument(null, null, [], [], [], bag.ToSortedList(), sourceName, baseDirectory);
    }

    public static ResolverDocument Read(SourceObject root, string? sourceName = null, string? baseDirectory = null, IEnumerable<Diagnostic>? prior = null)
    {
        var bag = new DiagnosticBag();
        if (prior is not null) bag.AddRange(prior);
        var reader = new Reader(root, bag);
        return reader.Read(sourceName, baseDirectory);
    }

    /// <summary>
    /// Validates inputs (resolver §6.1): unknown modifiers, invalid contexts, non-string values and missing inputs for modifiers
    /// without a default are errors. Matching is case-insensitive (§5.1). Returns the canonical inputs including defaults.
    /// </summary>
    public static IReadOnlyList<Diagnostic> ValidateInputs(ResolverDocument document, IReadOnlyDictionary<string, string> inputs, out Dictionary<string, string> effective)
    {
        var bag = new DiagnosticBag();
        effective = new Dictionary<string, string>(StringComparer.Ordinal);
        var modifiers = document.ActiveModifiers.ToList();
        foreach (var (key, value) in inputs)
        {
            var modifier = modifiers.FirstOrDefault(m => string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));
            if (modifier is null)
            {
                bag.Report(DiagnosticCatalog.InvalidResolverInput, $"Unknown modifier \"{key}\".", actual: key,
                    expected: string.Join(", ", modifiers.Select(static m => m.Name)));
                continue;
            }
            if (!modifier.TryGetContext(value, out var canonical, out _))
            {
                bag.Report(DiagnosticCatalog.InvalidResolverInput, $"Invalid context \"{value}\" for modifier \"{modifier.Name}\".", actual: value,
                    expected: string.Join(", ", modifier.ContextNames));
                continue;
            }
            effective[modifier.Name] = canonical;
        }
        foreach (var modifier in modifiers)
        {
            if (effective.ContainsKey(modifier.Name)) continue;
            if (modifier.Default is not null) effective[modifier.Name] = modifier.Default;
            else bag.Report(DiagnosticCatalog.InvalidResolverInput, $"Missing modifier \"{modifier.Name}\".",
                expected: string.Join(", ", modifier.ContextNames), remediation: $"Provide an input for \"{modifier.Name}\" or declare a default context.");
        }
        return bag.ToSortedList();
    }

    public static Task<ResolverResult> ResolveAsync(ResolverDocument document, IReadOnlyDictionary<string, string> inputs, IResolverSourceLoader loader,
        TokenResolutionOptions? options = null, CancellationToken cancellationToken = default)
        => new ResolverSession(document, loader).ResolveAsync(inputs, options, cancellationToken);

    private sealed class Reader(SourceObject root, DiagnosticBag bag)
    {
        public ResolverDocument Read(string? sourceName, string? baseDirectory)
        {
            foreach (var p in root.Properties)
            {
                if (!s_rootKeys.Contains(p.Name)) Warn($"Unknown resolver property '{p.Name}' is ignored.", p.NameLocation);
            }
            var version = root["version"];
            if (version is null) Error("The resolver document must declare \"version\": \"2025.10\".", root.Location);
            else if (version is not SourceString { Value: ResolverDocument.SupportedVersion })
                Error($"Unsupported resolver version {version.ToJson()}; this toolkit implements \"2025.10\".", version.Location);

            var name = (root["name"] as SourceString)?.Value;
            var description = (root["description"] as SourceString)?.Value;

            var sets = new List<ResolverSet>();
            if (root["sets"] is { } setsNode)
            {
                if (setsNode is SourceObject so)
                {
                    foreach (var p in so.Properties) if (ReadSet(p.Name, p.Value, inline: false) is { } s) sets.Add(s);
                }
                else Error("\"sets\" must be an object.", setsNode.Location);
            }

            var modifiers = new List<ResolverModifier>();
            if (root["modifiers"] is { } modsNode)
            {
                if (modsNode is SourceObject mo)
                {
                    foreach (var p in mo.Properties) if (ReadModifier(p.Name, p.Value, inline: false) is { } m) modifiers.Add(m);
                }
                else Error("\"modifiers\" must be an object.", modsNode.Location);
            }

            var order = new List<ResolutionOrderItem>();
            if (root["resolutionOrder"] is not SourceArray orderNode)
            {
                Error("The resolver document must declare a \"resolutionOrder\" array.", root["resolutionOrder"]?.Location ?? root.Location);
            }
            else
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in orderNode.Items)
                {
                    var entry = ReadOrderItem(item, sets, modifiers);
                    if (entry is null) continue;
                    if (!names.Add(entry.Name))
                    {
                        Error($"The name \"{entry.Name}\" appears more than once in resolutionOrder.", item.Location);
                        continue;
                    }
                    order.Add(entry);
                }
            }
            return new ResolverDocument(name, description, sets, modifiers, order, bag.ToSortedList(), sourceName, baseDirectory);
        }

        private ResolutionOrderItem? ReadOrderItem(SourceNode item, List<ResolverSet> sets, List<ResolverModifier> modifiers)
        {
            if (item is not SourceObject obj)
            {
                Error("resolutionOrder items must be reference objects or inline sets/modifiers.", item.Location);
                return null;
            }
            if (obj["$ref"] is SourceString { Value: var reference })
            {
                if (reference.StartsWith("#/sets/", StringComparison.Ordinal))
                {
                    var set = sets.FirstOrDefault(s => s.Name == Unescape(reference["#/sets/".Length..]));
                    if (set is null) { Error($"resolutionOrder references the unknown set '{reference}'.", obj.Location); return null; }
                    return new ResolutionOrderItem(set, isInline: false);
                }
                if (reference.StartsWith("#/modifiers/", StringComparison.Ordinal))
                {
                    var modifier = modifiers.FirstOrDefault(m => m.Name == Unescape(reference["#/modifiers/".Length..]));
                    if (modifier is null) { Error($"resolutionOrder references the unknown modifier '{reference}'.", obj.Location); return null; }
                    return new ResolutionOrderItem(modifier, isInline: false);
                }
                Error($"resolutionOrder reference '{reference}' must point to '#/sets/…' or '#/modifiers/…'.", obj.Location);
                return null;
            }
            var name = (obj["name"] as SourceString)?.Value;
            var type = (obj["type"] as SourceString)?.Value;
            if (name is null || type is null)
            {
                Error("Inline resolutionOrder items must declare both \"name\" and \"type\".", obj.Location);
                return null;
            }
            return type switch
            {
                "set" => ReadSet(name, obj, inline: true) is { } s ? new ResolutionOrderItem(s, isInline: true) : null,
                "modifier" => ReadModifier(name, obj, inline: true) is { } m ? new ResolutionOrderItem(m, isInline: true) : null,
                _ => ErrorNull($"Inline item \"{name}\" has type \"{type}\"; it must be \"set\" or \"modifier\".", obj.Location),
            };
        }

        private ResolverSet? ReadSet(string name, SourceNode node, bool inline)
        {
            if (node is not SourceObject obj) { Error($"Set \"{name}\" must be an object.", node.Location); return null; }
            CheckKeys(obj, $"set \"{name}\"", inline ? ["name", "type", "sources", "description", "$extensions"] : ["sources", "description", "$extensions"]);
            if (obj["sources"] is not SourceArray sources)
            {
                Error($"Set \"{name}\" must declare a \"sources\" array.", obj.Location);
                return null;
            }
            return new ResolverSet(name, ReadSources(sources, $"set \"{name}\""))
            {
                Description = (obj["description"] as SourceString)?.Value,
                Extensions = obj["$extensions"] as SourceObject,
            };
        }

        private ResolverModifier? ReadModifier(string name, SourceNode node, bool inline)
        {
            if (node is not SourceObject obj) { Error($"Modifier \"{name}\" must be an object.", node.Location); return null; }
            CheckKeys(obj, $"modifier \"{name}\"",
                inline ? ["name", "type", "contexts", "context", "default", "description", "$extensions"] : ["contexts", "context", "default", "description", "$extensions"]);
            var contextsNode = obj["contexts"];
            if (contextsNode is null && obj["context"] is { } typo)
            {
                Warn($"Modifier \"{name}\" uses \"context\"; the resolver module defines \"contexts\". It is read as \"contexts\".", typo.Location);
                contextsNode = typo;
            }
            if (contextsNode is not SourceObject contexts)
            {
                Error($"Modifier \"{name}\" must declare a \"contexts\" object.", obj.Location);
                return null;
            }
            if (contexts.Count == 0)
            {
                Error($"Modifier \"{name}\" must not have an empty \"contexts\" map.", contexts.Location);
                return null;
            }
            if (contexts.Count == 1)
            {
                bag.Report(DiagnosticCatalog.ResolverSingleContext, $"Modifier \"{name}\" declares a single context; modifiers should have two or more.",
                    location: contexts.Location);
            }
            var list = new List<KeyValuePair<string, IReadOnlyList<ResolverSource>>>();
            foreach (var c in contexts.Properties)
            {
                if (c.Value is not SourceArray arr)
                {
                    Error($"Context \"{c.Name}\" of modifier \"{name}\" must be an array of sources.", c.Value.Location);
                    continue;
                }
                list.Add(new(c.Name, ReadSources(arr, $"context \"{c.Name}\" of modifier \"{name}\"")));
            }
            string? def = null;
            if (obj["default"] is { } defNode)
            {
                if (defNode is SourceString { Value: var d } && contexts.ContainsKey(d)) def = d;
                else Error($"The default {defNode.ToJson()} of modifier \"{name}\" is not one of its contexts.", defNode.Location);
            }
            return new ResolverModifier(name, list)
            {
                Default = def,
                Description = (obj["description"] as SourceString)?.Value,
                Extensions = obj["$extensions"] as SourceObject,
            };
        }

        private List<ResolverSource> ReadSources(SourceArray array, string owner)
        {
            var result = new List<ResolverSource>();
            foreach (var item in array.Items)
            {
                if (item is not SourceObject obj)
                {
                    Error($"Sources of {owner} must be objects (inline tokens or reference objects).", item.Location);
                    continue;
                }
                if (obj.TryGetProperty("$ref", out var refNode))
                {
                    if (refNode is not SourceString { Value: var reference })
                    {
                        Error($"$ref in {owner} must be a string.", refNode.Location);
                        continue;
                    }
                    if (reference.StartsWith("#/modifiers", StringComparison.Ordinal))
                    {
                        Error($"{owner} references a modifier ('{reference}'); only resolutionOrder may reference modifiers.", refNode.Location);
                        continue;
                    }
                    if (reference.StartsWith("#/resolutionOrder", StringComparison.Ordinal))
                    {
                        Error($"{owner} references '{reference}'; reference objects must not point into resolutionOrder.", refNode.Location);
                        continue;
                    }
                    var overrides = obj.Without("$ref");
                    result.Add(ResolverSource.FromReference(reference, overrides.Count == 0 ? null : overrides, refNode.Location));
                }
                else
                {
                    result.Add(ResolverSource.FromTokens(obj, obj.Location));
                }
            }
            return result;
        }

        private void CheckKeys(SourceObject obj, string what, string[] allowed)
        {
            foreach (var p in obj.Properties)
            {
                if (Array.IndexOf(allowed, p.Name) < 0) Warn($"Unknown property '{p.Name}' on {what} is ignored.", p.NameLocation);
            }
        }

        private static string Unescape(string segment) => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

        private void Error(string message, SourceLocation? location) => bag.Report(DiagnosticCatalog.InvalidResolverDocument, message, location: location);

        private ResolutionOrderItem? ErrorNull(string message, SourceLocation? location)
        {
            Error(message, location);
            return null;
        }

        private void Warn(string message, SourceLocation? location)
            => bag.Add(new Diagnostic(DiagnosticCatalog.InvalidResolverDocument.Id, DiagnosticSeverity.Warning, message, location: location));
    }
}

/// <summary>
/// Evaluates a resolver document repeatedly (for example once per theme mode) while caching loaded files.
/// Aliases are resolved only after all layers are merged (resolver §6.3).
/// </summary>
public sealed class ResolverSession(ResolverDocument document, IResolverSourceLoader loader)
{
    private readonly Dictionary<string, SourceParseResult> _files = new(StringComparer.Ordinal);

    public ResolverDocument Document { get; } = document;

    public async Task<ResolverResult> ResolveAsync(IReadOnlyDictionary<string, string> inputs, TokenResolutionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var bag = new DiagnosticBag();
        bag.AddRange(Document.Diagnostics);
        bag.AddRange(DtcgResolver.ValidateInputs(Document, inputs, out var effective));
        var layers = new List<SourceObject>();
        if (!bag.HasErrors)
        {
            foreach (var item in Document.ResolutionOrder)
            {
                IReadOnlyList<ResolverSource> sources;
                if (item.Set is not null) sources = item.Set.Sources;
                else if (effective.TryGetValue(item.Modifier!.Name, out var context) && item.Modifier.TryGetContext(context, out _, out var contextSources)) sources = contextSources;
                else continue;
                foreach (var source in sources) layers.AddRange(await ExpandAsync(source, [], bag, cancellationToken).ConfigureAwait(false));
            }
        }
        var merged = TokenTreeMerger.Merge(layers);
        var document = DtcgReader.Read(merged, Document.SourceName);
        var resolved = TokenResolver.Resolve(document, options, cancellationToken);
        bag.AddRange(resolved.Diagnostics);
        var all = bag.ToSortedList().Distinct().ToList();
        return new ResolverResult(effective, merged, resolved, all);
    }

    /// <summary>Returns the raw token trees contributed by one context of a modifier (used for completeness analysis).</summary>
    public async Task<IReadOnlyList<SourceObject>> GetContextLayersAsync(ResolverModifier modifier, string context, DiagnosticBag bag, CancellationToken cancellationToken = default)
    {
        if (!modifier.TryGetContext(context, out _, out var sources)) return [];
        var layers = new List<SourceObject>();
        foreach (var s in sources) layers.AddRange(await ExpandAsync(s, [], bag, cancellationToken).ConfigureAwait(false));
        return layers;
    }

    public async Task<IReadOnlyList<SourceObject>> GetSetLayersAsync(ResolverSet set, DiagnosticBag bag, CancellationToken cancellationToken = default)
    {
        var layers = new List<SourceObject>();
        foreach (var s in set.Sources) layers.AddRange(await ExpandAsync(s, [], bag, cancellationToken).ConfigureAwait(false));
        return layers;
    }

    private async Task<IReadOnlyList<SourceObject>> ExpandAsync(ResolverSource source, HashSet<string> visiting, DiagnosticBag bag, CancellationToken cancellationToken)
    {
        if (source.Inline is not null) return [source.Inline];
        var reference = source.Reference!;
        List<SourceObject> result;
        if (reference.StartsWith("#/sets/", StringComparison.Ordinal))
        {
            var name = reference["#/sets/".Length..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            var set = Document.FindSet(name);
            if (set is null)
            {
                bag.Report(DiagnosticCatalog.InvalidResolverDocument, $"Reference '{reference}' points to an unknown set.", location: source.Location, actual: reference);
                return [];
            }
            if (!visiting.Add(reference))
            {
                bag.Report(DiagnosticCatalog.InvalidResolverDocument, $"Circular set reference: {string.Join(" -> ", visiting.Append(reference))}.",
                    location: source.Location, referenceChain: [.. visiting, reference]);
                return [];
            }
            result = [];
            foreach (var s in set.Sources) result.AddRange(await ExpandAsync(s, visiting, bag, cancellationToken).ConfigureAwait(false));
            visiting.Remove(reference);
        }
        else if (reference.StartsWith('#'))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConstruct,
                $"Same-document reference '{reference}' is not supported in sources; only '#/sets/…' references are.", location: source.Location, actual: reference);
            return [];
        }
        else
        {
            var hash = reference.IndexOf('#');
            var file = hash < 0 ? reference : reference[..hash];
            var fragment = hash < 0 ? null : reference[hash..];
            if (!_files.TryGetValue(file, out var parsed))
            {
                parsed = await loader.LoadAsync(file, Document.BaseDirectory, cancellationToken).ConfigureAwait(false);
                _files[file] = parsed;
                bag.AddRange(parsed.Diagnostics);
            }
            if (parsed.Root is not SourceObject content)
            {
                if (parsed.Root is not null)
                    bag.Report(DiagnosticCatalog.InvalidResolverDocument, $"Referenced file '{file}' must contain a JSON object.", location: source.Location);
                return [];
            }
            if (fragment is not null)
            {
                if (!JsonPointer.TryParse(fragment, out var segments, out var error) || JsonPointer.Navigate(content, segments) is not SourceObject target)
                {
                    bag.Report(DiagnosticCatalog.InvalidResolverDocument,
                        $"Fragment '{fragment}' of '{reference}' does not resolve to an object{(error is null ? "" : $": {error}")}.", location: source.Location, actual: reference);
                    return [];
                }
                content = target;
            }
            result = [content];
        }
        if (source.Overrides is not null && result.Count > 0)
        {
            // Keys next to $ref override shallowly: objects and arrays replace, they are not merged (resolver §4.2.2).
            var last = result[^1];
            foreach (var p in source.Overrides.Properties) last = last.With(p.Name, p.Value);
            result[^1] = last;
        }
        return result;
    }
}
