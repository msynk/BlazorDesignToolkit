using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens;

/// <summary>
/// Reads DTCG 2025.10 token documents into the authored model (<see cref="TokenDocument"/>), reporting structural problems.
/// Value shapes and references are checked later by <see cref="Resolution.TokenResolver"/>.
/// </summary>
public static class DtcgReader
{
    /// <summary>Root-level keys that are tolerated without warning because they are widespread JSON conventions.</summary>
    private static readonly HashSet<string> s_rootConventions = new(StringComparer.Ordinal) { "$schema" };

    public static TokenDocument Read(string json, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        var parsed = SourceJsonParser.Parse(json, options, cancellationToken);
        return FromParseResult(parsed, options?.SourceName);
    }

    public static async Task<TokenDocument> ReadFileAsync(string path, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = (options ?? JsonParseOptions.Default) with { SourceName = options?.SourceName ?? path };
        var parsed = await SourceJsonParser.ParseFileAsync(path, options, cancellationToken).ConfigureAwait(false);
        return FromParseResult(parsed, options.SourceName);
    }

    public static async Task<TokenDocument> ReadAsync(Stream stream, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        var parsed = await SourceJsonParser.ParseAsync(stream, options, cancellationToken).ConfigureAwait(false);
        return FromParseResult(parsed, options?.SourceName);
    }

    private static TokenDocument FromParseResult(SourceParseResult parsed, string? sourceName)
    {
        if (parsed.Root is null)
        {
            return new TokenDocument(new TokenGroup("", TokenPath.Empty), null, parsed.Diagnostics, sourceName);
        }
        if (parsed.Root is not SourceObject root)
        {
            var bag = new DiagnosticBag();
            bag.AddRange(parsed.Diagnostics);
            bag.Report(DiagnosticCatalog.RootNotObject, "The root of a design token document must be a JSON object.",
                location: parsed.Root.Location, actual: parsed.Root.Kind.ToString());
            return new TokenDocument(new TokenGroup("", TokenPath.Empty), null, bag.ToSortedList(), sourceName);
        }
        var doc = Read(root, sourceName);
        return parsed.Diagnostics.Count == 0
            ? doc
            : new TokenDocument(doc.Root, root, [.. parsed.Diagnostics, .. doc.Diagnostics], sourceName);
    }

    /// <summary>Builds the authored model from an already parsed JSON object.</summary>
    public static TokenDocument Read(SourceObject root, string? sourceName = null)
    {
        var bag = new DiagnosticBag();
        var group = ReadGroup("", TokenPath.Empty, root, bag, isRoot: true);
        return new TokenDocument(group, root, bag.ToSortedList(), sourceName);
    }

    /// <summary>True when the object represents a token rather than a group.</summary>
    public static bool IsTokenObject(SourceObject obj)
        => obj.ContainsKey("$value") || (obj.ContainsKey("$ref") && !obj.Properties.Any(static p => !p.Name.StartsWith('$')));

    private static TokenGroup ReadGroup(string name, TokenPath path, SourceObject obj, DiagnosticBag bag, bool isRoot)
    {
        string? type = null, description = null, extends = null;
        TokenDeprecation? deprecation = null;
        SourceObject? extensions = null;
        SourceLocation? extendsLocation = null;
        var unknown = new List<SourceProperty>();
        var childEntries = new List<SourceProperty>();

        foreach (var p in obj.Properties)
        {
            if (!p.Name.StartsWith('$'))
            {
                childEntries.Add(p);
                continue;
            }
            switch (p.Name)
            {
                case "$type":
                    type = ReadType(p, path, bag);
                    break;
                case "$description":
                    description = ReadString(p, path, bag);
                    break;
                case "$extensions":
                    extensions = ReadExtensions(p, path, bag);
                    break;
                case "$deprecated":
                    deprecation = ReadDeprecated(p, path, bag);
                    break;
                case "$extends":
                    extends = ReadString(p, path, bag);
                    extendsLocation = p.Value.Location;
                    break;
                case TokenPath.RootTokenName:
                    childEntries.Add(p);
                    break;
                default:
                    if (isRoot && s_rootConventions.Contains(p.Name))
                    {
                        unknown.Add(p);
                        break;
                    }
                    bag.Report(DiagnosticCatalog.UnknownReservedProperty,
                        $"Unknown reserved property '{p.Name}' on group '{Display(path)}'. It is preserved but ignored.",
                        Display(path), p.NameLocation, actual: p.Name);
                    unknown.Add(p);
                    break;
            }
        }

        var group = new TokenGroup(name, path)
        {
            Type = type,
            Description = description,
            Extensions = extensions,
            Deprecation = deprecation,
            Extends = extends,
            ExtendsLocation = extendsLocation,
            UnknownReservedProperties = unknown,
            Location = obj.Location,
        };

        var caseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in childEntries)
        {
            var childPath = path.Append(entry.Name);
            if (entry.Name != TokenPath.RootTokenName && !IsValidName(entry.Name, out var reason))
            {
                bag.Report(DiagnosticCatalog.InvalidName, $"Invalid name '{entry.Name}' in '{Display(path)}': {reason}.",
                    childPath.ToString(), entry.NameLocation, actual: entry.Name,
                    remediation: "Names must not start with '$' and must not contain '{', '}' or '.'.");
                continue;
            }
            if (caseMap.TryGetValue(entry.Name, out var other) && other != entry.Name)
            {
                bag.Report(DiagnosticCatalog.NamesDifferOnlyByCase, $"'{entry.Name}' and '{other}' in '{Display(path)}' differ only by case.",
                    childPath.ToString(), entry.NameLocation);
            }
            caseMap.TryAdd(entry.Name, entry.Name);

            if (entry.Value is not SourceObject childObj)
            {
                bag.Report(DiagnosticCatalog.InvalidPropertyType,
                    $"'{childPath}' must be a token or group object, but is a JSON {entry.Value.Kind.ToString().ToLowerInvariant()}.",
                    childPath.ToString(), entry.Value.Location, expected: "object", actual: entry.Value.Kind.ToString());
                continue;
            }

            if (entry.Name == TokenPath.RootTokenName && !IsTokenObject(childObj))
            {
                bag.Report(DiagnosticCatalog.InvalidPropertyType, $"'{childPath}' is reserved for the group's root token and must contain $value.",
                    childPath.ToString(), entry.Value.Location);
                continue;
            }

            if (IsTokenObject(childObj)) group.AddChild(ReadToken(entry.Name, childPath, childObj, bag));
            else group.AddChild(ReadGroup(entry.Name, childPath, childObj, bag, isRoot: false));
        }

        if (!isRoot && group.Children.Count == 0 && extends is null && (type is not null || description is not null))
        {
            bag.Report(DiagnosticCatalog.PossiblyIncompleteToken,
                $"Group '{path}' has no tokens but declares {(type is not null ? "$type" : "$description")}; is a $value missing?",
                path.ToString(), obj.Location);
        }
        return group;
    }

    private static TokenDefinition ReadToken(string name, TokenPath path, SourceObject obj, DiagnosticBag bag)
    {
        string? type = null, description = null;
        TokenDeprecation? deprecation = null;
        SourceObject? extensions = null;
        SourceNode? value = null;
        var tokenLevelPointer = false;
        var unknown = new List<SourceProperty>();

        foreach (var p in obj.Properties)
        {
            switch (p.Name)
            {
                case "$value":
                    value = p.Value;
                    break;
                case "$ref":
                    if (obj.ContainsKey("$value"))
                    {
                        bag.Report(DiagnosticCatalog.UnsupportedConstruct,
                            $"Token '{path}' declares both $value and a token-level $ref; the $ref is ignored.", path.ToString(), p.NameLocation);
                        unknown.Add(p);
                    }
                    else
                    {
                        value = SourceObject.Create(("$ref", p.Value));
                        tokenLevelPointer = true;
                        if (p.Value is not SourceString)
                        {
                            bag.Report(DiagnosticCatalog.InvalidPropertyType, $"$ref of token '{path}' must be a string JSON Pointer.",
                                path.ToString(), p.Value.Location, expected: "string", actual: p.Value.Kind.ToString());
                        }
                    }
                    break;
                case "$type":
                    type = ReadType(p, path, bag);
                    break;
                case "$description":
                    description = ReadString(p, path, bag);
                    break;
                case "$extensions":
                    extensions = ReadExtensions(p, path, bag);
                    break;
                case "$deprecated":
                    deprecation = ReadDeprecated(p, path, bag);
                    break;
                case "$extends":
                    bag.Report(DiagnosticCatalog.InvalidExtends, $"$extends is only valid on groups, but '{path}' is a token.",
                        path.ToString(), p.NameLocation);
                    unknown.Add(p);
                    break;
                default:
                    if (p.Name.StartsWith('$'))
                    {
                        bag.Report(DiagnosticCatalog.UnknownReservedProperty,
                            $"Unknown reserved property '{p.Name}' on token '{path}'. It is preserved but ignored.", path.ToString(), p.NameLocation, actual: p.Name);
                        unknown.Add(p);
                    }
                    else
                    {
                        bag.Report(DiagnosticCatalog.TokenWithChildren,
                            $"Token '{path}' has $value and must not contain the child '{p.Name}'.", path.ToString(), p.NameLocation,
                            remediation: $"Move '{p.Name}' to a sibling group, or turn '{path}' into a group with a $root token.");
                    }
                    break;
            }
        }

        return new TokenDefinition(name, path, value ?? SourceNull.Instance)
        {
            Type = type,
            Description = description,
            Deprecation = deprecation,
            Extensions = extensions,
            UnknownReservedProperties = unknown,
            Location = obj.Location,
            IsTokenLevelPointer = tokenLevelPointer,
        };
    }

    public static bool IsValidName(string name, out string reason)
    {
        if (name.Length == 0) { reason = "names must not be empty"; return false; }
        if (name[0] == '$') { reason = "names must not begin with '$'"; return false; }
        if (name.Contains('{') || name.Contains('}')) { reason = "names must not contain '{' or '}'"; return false; }
        if (name.Contains('.')) { reason = "names must not contain '.'"; return false; }
        reason = "";
        return true;
    }

    private static string Display(TokenPath path) => path.IsEmpty ? "(root)" : path.ToString();

    private static string? ReadType(SourceProperty p, TokenPath path, DiagnosticBag bag)
    {
        if (p.Value is not SourceString s)
        {
            bag.Report(DiagnosticCatalog.InvalidPropertyType, $"$type on '{Display(path)}' must be a string.", Display(path), p.Value.Location,
                expected: "string", actual: p.Value.Kind.ToString());
            return null;
        }
        if (!TokenTypes.IsStandard(s.Value))
        {
            var suggestion = TokenTypes.All.FirstOrDefault(t => string.Equals(t, s.Value, StringComparison.OrdinalIgnoreCase));
            bag.Report(DiagnosticCatalog.UnknownType, $"Unknown $type '{s.Value}' on '{Display(path)}'.", Display(path), p.Value.Location,
                expected: string.Join(", ", TokenTypes.All.Order(StringComparer.Ordinal)), actual: s.Value,
                remediation: suggestion is not null ? $"Types are case-sensitive; did you mean '{suggestion}'?" : "Use one of the DTCG 2025.10 types.");
        }
        return s.Value;
    }

    private static string? ReadString(SourceProperty p, TokenPath path, DiagnosticBag bag)
    {
        if (p.Value is SourceString s) return s.Value;
        bag.Report(DiagnosticCatalog.InvalidPropertyType, $"{p.Name} on '{Display(path)}' must be a string.", Display(path), p.Value.Location,
            expected: "string", actual: p.Value.Kind.ToString());
        return null;
    }

    private static SourceObject? ReadExtensions(SourceProperty p, TokenPath path, DiagnosticBag bag)
    {
        if (p.Value is SourceObject o) return o;
        bag.Report(DiagnosticCatalog.InvalidPropertyType, $"$extensions on '{Display(path)}' must be an object.", Display(path), p.Value.Location,
            expected: "object", actual: p.Value.Kind.ToString());
        return null;
    }

    private static TokenDeprecation? ReadDeprecated(SourceProperty p, TokenPath path, DiagnosticBag bag)
    {
        switch (p.Value)
        {
            case SourceBoolean b: return b.Value ? TokenDeprecation.Deprecated : TokenDeprecation.NotDeprecated;
            case SourceString s: return new TokenDeprecation(true, s.Value);
            default:
                bag.Report(DiagnosticCatalog.InvalidPropertyType, $"$deprecated on '{Display(path)}' must be a boolean or a string.",
                    Display(path), p.Value.Location, expected: "boolean | string", actual: p.Value.Kind.ToString());
                return null;
        }
    }
}
