using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Validation;

/// <summary>
/// Reads validation rules and diagnostic policies from JSON (the format used by <c>bdt.config.json</c> and
/// <c>*.validation.json</c> files). Unknown properties are reported, so typos never disable a rule silently.
/// </summary>
public static class ValidationConfig
{
    public static ValidationOptions Read(SourceObject root, DiagnosticBag bag, DiagnosticPolicy? policy = null)
    {
        CheckKeys(root, bag, "validation", "$schema", "$description", "naming", "requiredTokens", "layers", "accessibility",
            "reportDuplicateValues", "checkScales", "reportDeprecatedTokens", "policy");
        var options = new ValidationOptions
        {
            Naming = Array(root["naming"], bag, ReadNaming),
            RequiredTokens = Array(root["requiredTokens"], bag, ReadRequired),
            Layers = root["layers"] is SourceObject l ? ReadLayers(l, bag) : null,
            Accessibility = root["accessibility"] is SourceObject a ? ReadAccessibility(a, bag) : new AccessibilityRules(),
            ReportDuplicateValues = Bool(root["reportDuplicateValues"], true),
            CheckScales = Bool(root["checkScales"], true),
            ReportDeprecatedTokens = Bool(root["reportDeprecatedTokens"], true),
            Policy = policy ?? (root["policy"] is SourceObject p ? ReadPolicy(p, bag) : DiagnosticPolicy.Default),
        };
        return options;
    }

    /// <summary>Reads <c>{ "failOn": "warning", "rules": { "BDT4003": "none" }, "suppressions": [ { "rule", "token", "component", "justification" } ] }</c>.</summary>
    public static DiagnosticPolicy ReadPolicy(SourceObject node, DiagnosticBag bag)
    {
        CheckKeys(node, bag, "policy", "failOn", "rules", "suppressions");
        var overrides = new Dictionary<string, DiagnosticSeverity>(StringComparer.Ordinal);
        if (node["rules"] is SourceObject rules)
        {
            foreach (var p in rules.Properties)
            {
                if (p.Value is SourceString s && TryParseSeverity(s.Value, out var severity)) overrides[p.Name] = severity;
                else bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Severity for '{p.Name}' must be one of none, info, warning, error.", location: p.Value.Location);
            }
        }
        var suppressions = new List<DiagnosticSuppression>();
        if (node["suppressions"] is SourceArray arr)
        {
            foreach (var item in arr.Items)
            {
                if (item is not SourceObject o || o["rule"] is not SourceString rule)
                {
                    bag.Report(DiagnosticCatalog.InvalidConfiguration, "Each suppression needs a \"rule\".", location: item.Location);
                    continue;
                }
                suppressions.Add(new DiagnosticSuppression(rule.Value, (o["justification"] as SourceString)?.Value ?? "",
                    (o["token"] as SourceString)?.Value, (o["component"] as SourceString)?.Value));
            }
        }
        var failOn = DiagnosticSeverity.Error;
        if (node["failOn"] is SourceString f && !TryParseSeverity(f.Value, out failOn))
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "\"failOn\" must be one of none, info, warning, error.", location: f.Location);
            failOn = DiagnosticSeverity.Error;
        }
        return new DiagnosticPolicy { SeverityOverrides = overrides, Suppressions = suppressions, FailOn = failOn };
    }

    public static bool TryParseSeverity(string value, out DiagnosticSeverity severity)
        => Enum.TryParse(value, ignoreCase: true, out severity) && Enum.IsDefined(severity);

    private static NamingRule? ReadNaming(SourceObject o, DiagnosticBag bag)
    {
        CheckKeys(o, bag, "naming rule", "pattern", "case", "segmentRegex", "pathRegex");
        NameCase? nameCase = null;
        if (o["case"] is SourceString c)
        {
            if (Enum.TryParse<NameCase>(c.Value.Replace("-", "").Replace("_", ""), true, out var parsed)) nameCase = parsed;
            else bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown name case '{c.Value}'. Use kebab-case, camelCase, snake_case or PascalCase.", location: c.Location);
        }
        return new NamingRule
        {
            Pattern = (o["pattern"] as SourceString)?.Value ?? "**",
            Case = nameCase,
            SegmentRegex = (o["segmentRegex"] as SourceString)?.Value,
            PathRegex = (o["pathRegex"] as SourceString)?.Value,
        };
    }

    private static RequiredToken? ReadRequired(SourceObject o, DiagnosticBag bag)
    {
        if (o["path"] is not SourceString path)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "Required tokens need a \"path\".", location: o.Location);
            return null;
        }
        return new RequiredToken(path.Value, (o["type"] as SourceString)?.Value);
    }

    private static TokenLayerConvention ReadLayers(SourceObject o, DiagnosticBag bag)
    {
        CheckKeys(o, bag, "layers", "primitive", "semantic", "component", "semanticMustAlias", "componentShouldUseSemantic");
        return new TokenLayerConvention
        {
            Primitive = Strings(o["primitive"]),
            Semantic = Strings(o["semantic"]),
            Component = Strings(o["component"]),
            SemanticMustAlias = Bool(o["semanticMustAlias"], true),
            ComponentShouldUseSemantic = Bool(o["componentShouldUseSemantic"], true),
        };
    }

    private static AccessibilityRules ReadAccessibility(SourceObject o, DiagnosticBag bag)
    {
        CheckKeys(o, bag, "accessibility", "pairs", "pairings", "focusIndicators", "backdrop", "includeAaa");
        return new AccessibilityRules
        {
            Pairs = Array(o["pairs"], bag, (p, b) => p["foreground"] is SourceString fg && p["background"] is SourceString bg
                ? new ContrastPair(fg.Value, bg.Value, Kind(p, b), Level(p, b))
                : Invalid<ContrastPair>(p, b, "Contrast pairs need \"foreground\" and \"background\".")),
            Pairings = Array(o["pairings"], bag, (p, b) => p["background"] is SourceString bg && p["foreground"] is SourceString fg
                ? new ContrastPairing(bg.Value, fg.Value, Kind(p, b), Level(p, b))
                : Invalid<ContrastPairing>(p, b, "Pairings need \"background\" and \"foreground\" patterns.")),
            FocusIndicators = Array(o["focusIndicators"], bag, (p, b) => p["indicator"] is SourceString ind
                ? new FocusIndicatorRule(ind.Value, Strings(p["backgrounds"]))
                : Invalid<FocusIndicatorRule>(p, b, "Focus indicator rules need an \"indicator\".")),
            Backdrop = (o["backdrop"] as SourceString)?.Value,
            IncludeAaa = Bool(o["includeAaa"], false),
        };
    }

    private static ContrastKind Kind(SourceObject o, DiagnosticBag bag)
    {
        if (o["kind"] is not SourceString k) return ContrastKind.Text;
        if (Enum.TryParse<ContrastKind>(k.Value.Replace("-", ""), true, out var kind)) return kind;
        bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown contrast kind '{k.Value}'. Use text, large-text, non-text or focus.", location: k.Location);
        return ContrastKind.Text;
    }

    private static ConformanceLevel Level(SourceObject o, DiagnosticBag bag)
    {
        if (o["level"] is not SourceString l) return ConformanceLevel.AA;
        if (Enum.TryParse<ConformanceLevel>(l.Value, true, out var level)) return level;
        bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown conformance level '{l.Value}'. Use AA or AAA.", location: l.Location);
        return ConformanceLevel.AA;
    }

    private static T? Invalid<T>(SourceObject o, DiagnosticBag bag, string message) where T : class
    {
        bag.Report(DiagnosticCatalog.InvalidConfiguration, message, location: o.Location);
        return null;
    }

    private static IReadOnlyList<T> Array<T>(SourceNode? node, DiagnosticBag bag, Func<SourceObject, DiagnosticBag, T?> read) where T : class
    {
        if (node is null) return [];
        if (node is not SourceArray arr)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "Expected an array.", location: node.Location);
            return [];
        }
        var list = new List<T>();
        foreach (var item in arr.Items)
        {
            if (item is SourceObject o && read(o, bag) is { } value) list.Add(value);
            else if (item is not SourceObject) bag.Report(DiagnosticCatalog.InvalidConfiguration, "Expected an object.", location: item.Location);
        }
        return list;
    }

    public static IReadOnlyList<string> Strings(SourceNode? node)
        => node switch
        {
            SourceArray a => a.Items.OfType<SourceString>().Select(static s => s.Value).ToList(),
            SourceString s => [s.Value],
            _ => [],
        };

    private static bool Bool(SourceNode? node, bool fallback) => node is SourceBoolean b ? b.Value : fallback;

    public static void CheckKeys(SourceObject o, DiagnosticBag bag, string what, params string[] allowed)
    {
        foreach (var p in o.Properties)
        {
            if (System.Array.IndexOf(allowed, p.Name) < 0)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Unknown property '{p.Name}' in {what}.", location: p.NameLocation,
                    expected: string.Join(", ", allowed.Where(static a => !a.StartsWith('$'))), actual: p.Name);
            }
        }
    }
}
