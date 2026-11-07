using System.Text.Json;

namespace BlazorDesignToolkit.Tokens.Diagnostics;

/// <summary>Machine-readable JSON representation of diagnostics (stable property names, deterministic order).</summary>
public static class DiagnosticJson
{
    public static void Write(Utf8JsonWriter writer, Diagnostic d)
    {
        writer.WriteStartObject();
        writer.WriteString("ruleId", d.RuleId);
        writer.WriteString("severity", d.Severity.ToString().ToLowerInvariant());
        if (d.Category is not null) writer.WriteString("category", d.Category);
        writer.WriteString("message", d.Message);
        if (d.TokenPath is not null) writer.WriteString("token", d.TokenPath);
        if (d.Component is not null) writer.WriteString("component", d.Component);
        if (d.Scope is not null) writer.WriteString("scope", d.Scope);
        if (d.Location is not null)
        {
            writer.WriteStartObject("location");
            if (d.Location.Source is not null) writer.WriteString("source", d.Location.Source);
            writer.WriteNumber("line", d.Location.Line);
            writer.WriteNumber("column", d.Location.Column);
            writer.WriteEndObject();
        }
        if (d.Expected is not null) writer.WriteString("expected", d.Expected);
        if (d.Actual is not null) writer.WriteString("actual", d.Actual);
        if (d.Remediation is not null) writer.WriteString("remediation", d.Remediation);
        if (d.ReferenceChain.Count > 0)
        {
            writer.WriteStartArray("referenceChain");
            foreach (var r in d.ReferenceChain) writer.WriteStringValue(r);
            writer.WriteEndArray();
        }
        if (d.IsSuppressed)
        {
            writer.WriteBoolean("suppressed", true);
            writer.WriteString("justification", d.SuppressionJustification);
        }
        writer.WriteEndObject();
    }

    public static void WriteArray(Utf8JsonWriter writer, string propertyName, IEnumerable<Diagnostic> diagnostics)
    {
        writer.WriteStartArray(propertyName);
        foreach (var d in diagnostics) Write(writer, d);
        writer.WriteEndArray();
    }

    /// <summary>Writes a summary object: counts, failure flag and the diagnostics (including suppressed ones).</summary>
    public static void WriteResult(Utf8JsonWriter writer, DiagnosticResult result)
    {
        writer.WriteStartObject("summary");
        writer.WriteNumber("errors", result.ErrorCount);
        writer.WriteNumber("warnings", result.WarningCount);
        writer.WriteNumber("info", result.InfoCount);
        writer.WriteNumber("suppressed", result.Suppressed.Count);
        writer.WriteString("failOn", result.FailOn.ToString().ToLowerInvariant());
        writer.WriteBoolean("failed", result.Failed);
        writer.WriteEndObject();
        WriteArray(writer, "diagnostics", result.Diagnostics);
        WriteArray(writer, "suppressed", result.Suppressed);
    }

    /// <summary>GitHub Actions workflow-command annotation for a diagnostic.</summary>
    public static string ToGitHubAnnotation(Diagnostic d)
    {
        var level = d.Severity switch { DiagnosticSeverity.Error => "error", DiagnosticSeverity.Warning => "warning", _ => "notice" };
        var props = new List<string>();
        if (d.Location?.Source is { } file) props.Add("file=" + Escape(file, true));
        if (d.Location is { } loc) { props.Add($"line={loc.Line}"); props.Add($"col={loc.Column}"); }
        props.Add("title=" + Escape($"{d.RuleId}{(d.TokenPath is null ? "" : " " + d.TokenPath)}", true));
        var message = d.Message + (d.Remediation is null ? "" : " " + d.Remediation);
        return $"::{level} {string.Join(',', props)}::{Escape(message, false)}";
    }

    private static string Escape(string value, bool property)
    {
        var s = value.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
        return property ? s.Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal) : s;
    }
}
