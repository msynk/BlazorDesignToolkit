using System.Text;
using System.Text.Json;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Cli;

internal enum ReportFormat
{
    Text,
    Json,
    Github,
    Sarif,
}

/// <summary>Writes diagnostics in human-readable or machine-readable form. All output is deterministic.</summary>
internal static class Reporting
{
    public static void WriteText(TextWriter writer, DiagnosticResult result, bool verbose)
    {
        foreach (var d in result.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Info && !verbose) continue;
            writer.WriteLine(d.ToString());
            if (d.Expected is not null || d.Actual is not null) writer.WriteLine($"    expected: {d.Expected ?? "-"}   actual: {d.Actual ?? "-"}");
            if (d.ReferenceChain.Count > 0) writer.WriteLine($"    chain: {string.Join(" -> ", d.ReferenceChain)}");
            if (d.Remediation is not null) writer.WriteLine($"    fix: {d.Remediation}");
        }
        var hiddenInfo = verbose ? 0 : result.InfoCount;
        writer.WriteLine();
        writer.WriteLine($"{result.ErrorCount} error(s), {result.WarningCount} warning(s), {result.InfoCount} info{(hiddenInfo > 0 ? " (use --verbose to show)" : "")}, {result.Suppressed.Count} suppressed.");
        writer.WriteLine(result.Failed ? $"FAILED (fail-on: {result.FailOn.ToString().ToLowerInvariant()})" : "PASSED");
    }

    public static void WriteGithub(TextWriter writer, DiagnosticResult result)
    {
        foreach (var d in result.Diagnostics) writer.WriteLine(DiagnosticJson.ToGitHubAnnotation(d));
        writer.WriteLine($"{result.ErrorCount} error(s), {result.WarningCount} warning(s), {result.InfoCount} info, {result.Suppressed.Count} suppressed.");
    }

    public static void WriteJson(TextWriter writer, DiagnosticResult result, Action<Utf8JsonWriter>? extra = null)
    {
        writer.Write(Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("tool", "bdt");
            DiagnosticJson.WriteResult(w, result);
            extra?.Invoke(w);
            w.WriteEndObject();
        }));
        writer.WriteLine();
    }

    /// <summary>SARIF 2.1.0 for code-scanning integrations.</summary>
    public static void WriteSarif(TextWriter writer, DiagnosticResult result, string baseDirectory)
    {
        writer.Write(Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            w.WriteString("version", "2.1.0");
            w.WriteStartArray("runs");
            w.WriteStartObject();
            w.WriteStartObject("tool");
            w.WriteStartObject("driver");
            w.WriteString("name", "bdt");
            w.WriteString("informationUri", "https://github.com/msynk/BlazorDesignToolkit");
            w.WriteStartArray("rules");
            foreach (var rule in result.Diagnostics.Select(static d => d.RuleId).Distinct().Order(StringComparer.Ordinal))
            {
                var descriptor = DiagnosticCatalog.TryGet(rule);
                w.WriteStartObject();
                w.WriteString("id", rule);
                if (descriptor is not null)
                {
                    w.WriteStartObject("shortDescription");
                    w.WriteString("text", descriptor.Title);
                    w.WriteEndObject();
                    w.WriteStartObject("fullDescription");
                    w.WriteString("text", descriptor.Description);
                    w.WriteEndObject();
                    w.WriteStartObject("defaultConfiguration");
                    w.WriteString("level", Level(descriptor.DefaultSeverity));
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartArray("results");
            foreach (var d in result.Diagnostics)
            {
                w.WriteStartObject();
                w.WriteString("ruleId", d.RuleId);
                w.WriteString("level", Level(d.Severity));
                w.WriteStartObject("message");
                w.WriteString("text", d.Message + (d.Remediation is null ? "" : " " + d.Remediation));
                w.WriteEndObject();
                if (d.Location?.Source is { } source)
                {
                    w.WriteStartArray("locations");
                    w.WriteStartObject();
                    w.WriteStartObject("physicalLocation");
                    w.WriteStartObject("artifactLocation");
                    var uri = Path.IsPathRooted(source) ? Path.GetRelativePath(baseDirectory, source) : source;
                    w.WriteString("uri", uri.Replace('\\', '/'));
                    w.WriteEndObject();
                    w.WriteStartObject("region");
                    w.WriteNumber("startLine", d.Location.Line);
                    w.WriteNumber("startColumn", d.Location.Column);
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteEndArray();
                }
                if (d.TokenPath is not null || d.Scope is not null)
                {
                    w.WriteStartObject("properties");
                    if (d.TokenPath is not null) w.WriteString("token", d.TokenPath);
                    if (d.Scope is not null) w.WriteString("scope", d.Scope);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }));
        writer.WriteLine();
    }

    private static string Level(DiagnosticSeverity s) => s switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "note",
    };

    public static string Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, SourceJsonWriter.CreateOptions(indented: true))) write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static void Write(TextWriter writer, ReportFormat format, DiagnosticResult result, string baseDirectory, bool verbose, Action<Utf8JsonWriter>? extraJson = null)
    {
        switch (format)
        {
            case ReportFormat.Json: WriteJson(writer, result, extraJson); break;
            case ReportFormat.Github: WriteGithub(writer, result); break;
            case ReportFormat.Sarif: WriteSarif(writer, result, baseDirectory); break;
            default: WriteText(writer, result, verbose); break;
        }
    }
}
