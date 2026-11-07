using System.Text;

namespace BlazorDesignToolkit.Tokens.Diagnostics;

/// <summary>Severity of a <see cref="Diagnostic"/>. <see cref="None"/> disables a rule.</summary>
public enum DiagnosticSeverity
{
    /// <summary>The rule is disabled; diagnostics with this severity are dropped.</summary>
    None = 0,
    /// <summary>Informational finding that never fails a build by default.</summary>
    Info = 1,
    /// <summary>Probable problem that should be reviewed.</summary>
    Warning = 2,
    /// <summary>Definite problem; the affected token or document is invalid.</summary>
    Error = 3,
}

/// <summary>A position in a source document. Line and column are 1-based; column counts characters, not bytes.</summary>
public sealed record SourceLocation(string? Source, int Line, int Column)
{
    public override string ToString()
        => Source is null ? $"({Line},{Column})" : $"{Source}({Line},{Column})";
}

/// <summary>
/// A single finding produced by parsing, resolution, validation, export or analysis.
/// Every diagnostic carries a stable <see cref="RuleId"/> listed in <see cref="DiagnosticCatalog"/>.
/// </summary>
public sealed class Diagnostic
{
    public Diagnostic(
        string ruleId,
        DiagnosticSeverity severity,
        string message,
        string? tokenPath = null,
        SourceLocation? location = null,
        string? expected = null,
        string? actual = null,
        string? remediation = null,
        IReadOnlyList<string>? referenceChain = null,
        string? component = null,
        string? category = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ruleId);
        ArgumentException.ThrowIfNullOrEmpty(message);
        RuleId = ruleId;
        Severity = severity;
        Message = message;
        TokenPath = tokenPath;
        Location = location;
        Expected = expected;
        Actual = actual;
        Remediation = remediation;
        ReferenceChain = referenceChain ?? [];
        Component = component;
        Category = category ?? DiagnosticCatalog.TryGet(ruleId)?.Category;
    }

    /// <summary>Stable rule identifier such as <c>BDT3002</c>.</summary>
    public string RuleId { get; }
    public DiagnosticSeverity Severity { get; private init; }
    public string Message { get; }
    /// <summary>Dot-separated token path affected by the finding, if any.</summary>
    public string? TokenPath { get; }
    /// <summary>Component affected by the finding (coverage and contract rules).</summary>
    public string? Component { get; }
    public SourceLocation? Location { get; }
    public string? Expected { get; }
    public string? Actual { get; }
    public string? Remediation { get; }
    /// <summary>For reference problems: the chain of token paths that led to the finding.</summary>
    public IReadOnlyList<string> ReferenceChain { get; }
    public string? Category { get; }
    /// <summary>True when a configured suppression matched this diagnostic.</summary>
    public bool IsSuppressed { get; private init; }
    public string? SuppressionJustification { get; private init; }
    /// <summary>Optional evaluation scope, for example the theme and mode a contrast check ran in.</summary>
    public string? Scope { get; private init; }

    public Diagnostic WithSeverity(DiagnosticSeverity severity) => Copy(severity, IsSuppressed, SuppressionJustification, Scope);

    public Diagnostic AsSuppressed(string justification) => Copy(Severity, true, justification, Scope);

    public Diagnostic WithScope(string? scope) => Copy(Severity, IsSuppressed, SuppressionJustification, scope);

    private Diagnostic Copy(DiagnosticSeverity severity, bool suppressed, string? justification, string? scope)
        => new(RuleId, severity, Message, TokenPath, Location, Expected, Actual, Remediation, ReferenceChain, Component, Category)
        {
            IsSuppressed = suppressed,
            SuppressionJustification = justification,
            Scope = scope,
        };

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Location is not null) sb.Append(Location).Append(": ");
        sb.Append(Severity.ToString().ToLowerInvariant()).Append(' ').Append(RuleId).Append(": ");
        if (Scope is not null) sb.Append('[').Append(Scope).Append("] ");
        sb.Append(Message);
        if (TokenPath is not null && !Message.Contains(TokenPath, StringComparison.Ordinal)) sb.Append(" [").Append(TokenPath).Append(']');
        return sb.ToString();
    }

    /// <summary>Deterministic ordering: source, line, column, token path, rule id, message.</summary>
    public static readonly IComparer<Diagnostic> Comparer = Comparer<Diagnostic>.Create(static (a, b) =>
    {
        var c = string.CompareOrdinal(a.Location?.Source, b.Location?.Source);
        if (c != 0) return c;
        c = (a.Location?.Line ?? int.MaxValue).CompareTo(b.Location?.Line ?? int.MaxValue);
        if (c != 0) return c;
        c = (a.Location?.Column ?? int.MaxValue).CompareTo(b.Location?.Column ?? int.MaxValue);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.TokenPath, b.TokenPath);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Scope, b.Scope);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Component, b.Component);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.RuleId, b.RuleId);
        return c != 0 ? c : string.CompareOrdinal(a.Message, b.Message);
    });
}

/// <summary>Collects diagnostics during an operation.</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;

    public bool HasErrors => _items.Exists(static d => d.Severity == DiagnosticSeverity.Error && !d.IsSuppressed);

    public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);

    /// <summary>Creates a diagnostic using the catalog's default severity for <paramref name="descriptor"/>.</summary>
    public Diagnostic Report(
        DiagnosticDescriptor descriptor,
        string message,
        string? tokenPath = null,
        SourceLocation? location = null,
        string? expected = null,
        string? actual = null,
        string? remediation = null,
        IReadOnlyList<string>? referenceChain = null,
        string? component = null)
    {
        var d = new Diagnostic(descriptor.Id, descriptor.DefaultSeverity, message, tokenPath, location, expected, actual,
            remediation ?? descriptor.DefaultRemediation, referenceChain, component, descriptor.Category);
        _items.Add(d);
        return d;
    }

    public IReadOnlyList<Diagnostic> ToSortedList()
    {
        var copy = _items.ToList();
        copy.Sort(Diagnostic.Comparer);
        return copy;
    }
}
