using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Resolution;

/// <summary>A token source inside a resolver set or modifier context: inline tokens or a <c>$ref</c> reference object.</summary>
public sealed class ResolverSource
{
    private ResolverSource(SourceObject? inline, string? reference, SourceObject? overrides, SourceLocation? location)
    {
        Inline = inline;
        Reference = reference;
        Overrides = overrides;
        Location = location;
    }

    public static ResolverSource FromTokens(SourceObject tokens, SourceLocation? location = null) => new(tokens, null, null, location);

    public static ResolverSource FromReference(string reference, SourceObject? overrides = null, SourceLocation? location = null)
        => new(null, reference, overrides, location);

    public SourceObject? Inline { get; }
    public string? Reference { get; }
    /// <summary>Keys declared next to <c>$ref</c>; they override the referenced content shallowly (resolver §4.2.2).</summary>
    public SourceObject? Overrides { get; }
    public SourceLocation? Location { get; }
    public bool IsReference => Reference is not null;
}

public sealed class ResolverSet(string name, IReadOnlyList<ResolverSource> sources)
{
    public string Name { get; } = name;
    public IReadOnlyList<ResolverSource> Sources { get; } = sources;
    public string? Description { get; init; }
    public SourceObject? Extensions { get; init; }
}

public sealed class ResolverModifier(string name, IReadOnlyList<KeyValuePair<string, IReadOnlyList<ResolverSource>>> contexts)
{
    public string Name { get; } = name;
    /// <summary>Contexts in declaration order.</summary>
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<ResolverSource>>> Contexts { get; } = contexts;
    public string? Default { get; init; }
    public string? Description { get; init; }
    public SourceObject? Extensions { get; init; }

    public IEnumerable<string> ContextNames => Contexts.Select(static c => c.Key);

    /// <summary>Case-insensitive context lookup (inputs SHOULD be case-insensitive, resolver §5.1).</summary>
    public bool TryGetContext(string name, out string canonicalName, out IReadOnlyList<ResolverSource> sources)
    {
        foreach (var c in Contexts)
        {
            if (string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                canonicalName = c.Key;
                sources = c.Value;
                return true;
            }
        }
        canonicalName = name;
        sources = [];
        return false;
    }
}

/// <summary>An entry of <c>resolutionOrder</c>: a set or modifier, either referenced (<c>#/sets/x</c>) or declared inline.</summary>
public sealed class ResolutionOrderItem
{
    public ResolutionOrderItem(ResolverSet set, bool isInline) { Set = set; IsInline = isInline; }
    public ResolutionOrderItem(ResolverModifier modifier, bool isInline) { Modifier = modifier; IsInline = isInline; }

    public ResolverSet? Set { get; }
    public ResolverModifier? Modifier { get; }
    public bool IsInline { get; }
    public string Name => Set?.Name ?? Modifier!.Name;
}

/// <summary>A DTCG 2025.10 resolver document (sets, modifiers and resolution order).</summary>
public sealed class ResolverDocument
{
    public const string SupportedVersion = "2025.10";

    internal ResolverDocument(string? name, string? description, IReadOnlyList<ResolverSet> sets, IReadOnlyList<ResolverModifier> modifiers,
        IReadOnlyList<ResolutionOrderItem> order, IReadOnlyList<Diagnostic> diagnostics, string? sourceName, string? baseDirectory, SourceObject? root = null)
    {
        Root = root;
        Name = name;
        Description = description;
        Sets = sets;
        Modifiers = modifiers;
        ResolutionOrder = order;
        Diagnostics = diagnostics;
        SourceName = sourceName;
        BaseDirectory = baseDirectory;
    }

    public string? Name { get; }
    public string? Description { get; }
    public IReadOnlyList<ResolverSet> Sets { get; }
    public IReadOnlyList<ResolverModifier> Modifiers { get; }
    public IReadOnlyList<ResolutionOrderItem> ResolutionOrder { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public string? SourceName { get; }
    /// <summary>The parsed document, used to resolve same-document pointers such as <c>#/$defs/brand</c> in sources.</summary>
    public SourceObject? Root { get; }
    /// <summary>Directory used to resolve relative file references.</summary>
    public string? BaseDirectory { get; }
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Modifiers that participate in the resolution order (referenced or inline).</summary>
    public IEnumerable<ResolverModifier> ActiveModifiers => ResolutionOrder.Where(static i => i.Modifier is not null).Select(static i => i.Modifier!);

    public ResolverSet? FindSet(string name) => Sets.FirstOrDefault(s => s.Name == name);
    public ResolverModifier? FindModifier(string name) => Modifiers.FirstOrDefault(m => m.Name == name);
}

/// <summary>Loads token files referenced from resolver documents.</summary>
public interface IResolverSourceLoader
{
    /// <summary>Loads the JSON document at <paramref name="reference"/> (a relative path, without fragment).</summary>
    Task<SourceParseResult> LoadAsync(string reference, string? baseDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Loads referenced files from disk. References must stay inside <see cref="RootDirectory"/>; absolute paths,
/// parent-directory escapes and remote URLs are rejected, so a resolver document cannot read arbitrary files or reach the network.
/// Wrap it in an <see cref="HttpResolverSourceLoader"/> to allow specific remote URLs.
/// </summary>
public sealed class FileSystemResolverSourceLoader(string rootDirectory, JsonParseOptions? parseOptions = null) : IResolverSourceLoader
{
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public async Task<SourceParseResult> LoadAsync(string reference, string? baseDirectory, CancellationToken cancellationToken)
    {
        var bag = new DiagnosticBag();
        if (reference.Contains("://", StringComparison.Ordinal))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConstruct, $"Remote reference '{reference}' is not allowed: remote loading is off by default.",
                actual: reference, remediation: "Download the file and reference it with a relative path, or allow its URL prefix (HttpResolverSourceLoader; CLI: \"remote\": { \"allow\": [ … ] }).");
            return new SourceParseResult(null, bag.Items);
        }
        if (Path.IsPathRooted(reference))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConstruct, $"Absolute file reference '{reference}' is not allowed; use a path relative to the resolver document.", actual: reference);
            return new SourceParseResult(null, bag.Items);
        }
        var full = Path.GetFullPath(Path.Combine(baseDirectory ?? RootDirectory, reference));
        var root = RootDirectory.EndsWith(Path.DirectorySeparatorChar) ? RootDirectory : RootDirectory + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConstruct, $"Reference '{reference}' resolves outside the allowed root directory '{RootDirectory}'.", actual: full);
            return new SourceParseResult(null, bag.Items);
        }
        if (!File.Exists(full))
        {
            bag.Report(DiagnosticCatalog.FileNotFound, $"Referenced file '{reference}' was not found ({full}).", actual: full);
            return new SourceParseResult(null, bag.Items);
        }
        return await SourceJsonParser.ParseFileAsync(full, (parseOptions ?? JsonParseOptions.Default) with { SourceName = full }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A loader backed by an in-memory map of relative paths to JSON text (tests, browser scenarios).</summary>
public sealed class InMemoryResolverSourceLoader(IReadOnlyDictionary<string, string> files, JsonParseOptions? parseOptions = null) : IResolverSourceLoader
{
    public Task<SourceParseResult> LoadAsync(string reference, string? baseDirectory, CancellationToken cancellationToken)
    {
        var key = reference.Replace('\\', '/');
        if (key.StartsWith("./", StringComparison.Ordinal)) key = key[2..];
        if (files.TryGetValue(key, out var json))
        {
            return Task.FromResult(SourceJsonParser.Parse(json, (parseOptions ?? JsonParseOptions.Default) with { SourceName = key }, cancellationToken));
        }
        var bag = new DiagnosticBag();
        bag.Report(DiagnosticCatalog.FileNotFound, $"Referenced file '{reference}' was not found.", actual: reference);
        return Task.FromResult(new SourceParseResult(null, bag.Items));
    }
}
