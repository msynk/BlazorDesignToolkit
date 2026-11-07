using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens.Export;

/// <summary>A mapping from an integration-specific target (a framework variable) to a token.</summary>
public sealed record TargetMapping(string Target, string Token, SourceLocation? Location = null);

/// <summary>How integration outputs express values.</summary>
public enum IntegrationValueMode
{
    /// <summary>Reference the toolkit's runtime custom properties with <c>var(--…)</c>, so theme switching keeps working.</summary>
    Reference,
    /// <summary>Emit literal values resolved from the given token set (static, per theme).</summary>
    Literal,
}

/// <summary>Shared helpers for integration exporters (mapping tables, value formatting, type checks).</summary>
public static class IntegrationSupport
{
    /// <summary>Reads <c>{ "mappings": { "target": "token.path" } }</c> from exporter configuration.</summary>
    public static IReadOnlyList<TargetMapping> ReadTargetMappings(SourceObject? configuration, DiagnosticBag bag, string property = "mappings")
    {
        var list = new List<TargetMapping>();
        if (configuration?[property] is not { } node) return list;
        if (node is not SourceObject obj)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, $"\"{property}\" must be an object of target names to token paths.", location: node.Location);
            return list;
        }
        foreach (var p in obj.Properties)
        {
            if (p.Value is SourceString s) list.Add(new TargetMapping(p.Name, s.Value.Trim('{', '}'), p.NameLocation));
            else bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Mapping for '{p.Name}' must be a token path string.", location: p.Value.Location);
        }
        return list;
    }

    /// <summary>
    /// Validates that <paramref name="mapping"/> points to a valid token whose type is one of <paramref name="allowedTypes"/>.
    /// Returns the token, or null after reporting BDT7004/BDT7005.
    /// </summary>
    public static ResolvedToken? Require(ResolvedTokenSet tokens, TargetMapping mapping, IReadOnlyCollection<string> allowedTypes, string integration, DiagnosticBag bag)
    {
        var token = tokens[TokenPath.Parse(mapping.Token)];
        if (token is null)
        {
            bag.Report(DiagnosticCatalog.InvalidMapping, $"{integration} mapping '{mapping.Target}' references unknown token '{mapping.Token}'.", mapping.Token, mapping.Location,
                actual: mapping.Token);
            return null;
        }
        if (!token.IsValid) return null; // already reported by resolution
        if (!allowedTypes.Contains(token.Type!))
        {
            bag.Report(DiagnosticCatalog.MappingTypeMismatch,
                $"{integration} '{mapping.Target}' requires {string.Join(" or ", allowedTypes)}, but '{mapping.Token}' is a {token.Type}.",
                mapping.Token, mapping.Location, expected: string.Join(" | ", allowedTypes), actual: token.Type);
            return null;
        }
        return token;
    }

    /// <summary>Formats a token as <c>var(--name)</c> (reference mode) or as a literal CSS value.</summary>
    public static string? Format(ResolvedToken token, IntegrationValueMode mode, CssExportOptions css, DiagnosticBag bag)
        => mode == IntegrationValueMode.Reference
            ? $"var({CssExporter.GetVariableName(token.Path, css)})"
            : CssExporter.FormatValue(token.Value!, css with { References = CssReferenceMode.Resolve }, bag, token.Path.ToString());
}
