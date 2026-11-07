namespace BlazorDesignToolkit.Tokens.Diagnostics;

/// <summary>
/// Suppresses a rule, optionally only for token paths matching <see cref="TokenPattern"/>.
/// A justification is mandatory so that suppressions stay reviewable.
/// </summary>
public sealed record DiagnosticSuppression(string RuleId, string Justification, string? TokenPattern = null, string? Component = null)
{
    public bool Matches(Diagnostic diagnostic)
    {
        if (!string.Equals(RuleId, diagnostic.RuleId, StringComparison.Ordinal) && RuleId != "*") return false;
        if (TokenPattern is not null && (diagnostic.TokenPath is null || !TokenPathPattern.IsMatch(TokenPattern, diagnostic.TokenPath))) return false;
        if (Component is not null && !string.Equals(Component, diagnostic.Component, StringComparison.Ordinal)) return false;
        return true;
    }
}

/// <summary>Severity overrides, suppressions and the failure threshold applied to a set of diagnostics.</summary>
public sealed class DiagnosticPolicy
{
    public static DiagnosticPolicy Default { get; } = new();

    /// <summary>Per-rule severity overrides. <see cref="DiagnosticSeverity.None"/> disables a rule.</summary>
    public Dictionary<string, DiagnosticSeverity> SeverityOverrides { get; init; } = new(StringComparer.Ordinal);

    public List<DiagnosticSuppression> Suppressions { get; init; } = [];

    /// <summary>The lowest severity that makes <see cref="DiagnosticResult.Failed"/> true.</summary>
    public DiagnosticSeverity FailOn { get; init; } = DiagnosticSeverity.Error;

    /// <summary>Returns configuration problems in the policy itself (for example suppressions without justification).</summary>
    public IReadOnlyList<Diagnostic> Validate()
    {
        var bag = new DiagnosticBag();
        foreach (var s in Suppressions)
        {
            if (string.IsNullOrWhiteSpace(s.Justification))
            {
                bag.Report(DiagnosticCatalog.SuppressionWithoutJustification,
                    $"Suppression of {s.RuleId}{(s.TokenPattern is null ? "" : $" for '{s.TokenPattern}'")} has no justification.");
            }
            if (s.RuleId != "*" && DiagnosticCatalog.TryGet(s.RuleId) is null)
            {
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Suppression references unknown rule '{s.RuleId}'.");
            }
        }
        foreach (var id in SeverityOverrides.Keys)
        {
            if (DiagnosticCatalog.TryGet(id) is null)
                bag.Report(DiagnosticCatalog.InvalidConfiguration, $"Severity override references unknown rule '{id}'.");
        }
        return bag.ToSortedList();
    }

    public DiagnosticResult Apply(IEnumerable<Diagnostic> diagnostics)
    {
        var effective = new List<Diagnostic>();
        var suppressed = new List<Diagnostic>();
        foreach (var d in diagnostics)
        {
            var item = SeverityOverrides.TryGetValue(d.RuleId, out var severity) ? d.WithSeverity(severity) : d;
            if (item.Severity == DiagnosticSeverity.None) continue;
            var suppression = Suppressions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Justification) && s.Matches(item));
            if (suppression is not null)
            {
                suppressed.Add(item.AsSuppressed(suppression.Justification));
                continue;
            }
            effective.Add(item);
        }
        effective.Sort(Diagnostic.Comparer);
        suppressed.Sort(Diagnostic.Comparer);
        return new DiagnosticResult(effective, suppressed, FailOn);
    }
}

/// <summary>Outcome of applying a <see cref="DiagnosticPolicy"/>.</summary>
public sealed class DiagnosticResult(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<Diagnostic> suppressed, DiagnosticSeverity failOn)
{
    /// <summary>Diagnostics after severity overrides, excluding disabled and suppressed ones, sorted deterministically.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public IReadOnlyList<Diagnostic> Suppressed { get; } = suppressed;
    public DiagnosticSeverity FailOn { get; } = failOn;

    public int ErrorCount => Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning);
    public int InfoCount => Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Info);

    public bool Failed => FailOn != DiagnosticSeverity.None && Diagnostics.Any(d => d.Severity >= FailOn);
}

/// <summary>Glob-style matching for dot-separated token paths. '*' matches one segment, '**' matches any number of segments.</summary>
public static class TokenPathPattern
{
    public static bool IsMatch(string pattern, string path)
    {
        var p = pattern.Split('.');
        var s = path.Split('.');
        return Match(p, 0, s, 0);
    }

    private static bool Match(string[] p, int pi, string[] s, int si)
    {
        while (true)
        {
            if (pi == p.Length) return si == s.Length;
            if (p[pi] == "**")
            {
                for (var k = si; k <= s.Length; k++)
                {
                    if (Match(p, pi + 1, s, k)) return true;
                }
                return false;
            }
            if (si == s.Length) return false;
            if (!SegmentMatch(p[pi], s[si])) return false;
            pi++;
            si++;
        }
    }

    private static bool SegmentMatch(string pattern, string segment)
    {
        if (pattern == "*") return true;
        if (!pattern.Contains('*')) return string.Equals(pattern, segment, StringComparison.Ordinal);
        // Simple wildcard within a segment, e.g. "on-*".
        var parts = pattern.Split('*');
        var index = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            var found = segment.IndexOf(part, index, StringComparison.Ordinal);
            if (found < 0 || (i == 0 && found != 0)) return false;
            index = found + part.Length;
        }
        return parts[^1].Length == 0 || segment.EndsWith(parts[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Matches <paramref name="path"/> against a pattern containing exactly one '*' capture inside the path and returns the captured text.
    /// </summary>
    public static bool TryCapture(string pattern, string path, out string capture)
    {
        capture = "";
        var star = pattern.IndexOf('*');
        if (star < 0 || pattern.IndexOf('*', star + 1) >= 0) return false;
        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];
        if (path.Length < prefix.Length + suffix.Length) return false;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal)) return false;
        capture = path[prefix.Length..(path.Length - suffix.Length)];
        return capture.Length > 0;
    }
}
