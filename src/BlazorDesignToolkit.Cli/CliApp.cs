using System.CommandLine;
using System.Text.Json;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Analysis;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Validation;

namespace BlazorDesignToolkit.Cli;

/// <summary>Exit codes follow common CLI conventions.</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    /// <summary>A quality gate failed (validation, out-of-date outputs, coverage threshold, breaking changes).</summary>
    public const int GateFailed = 1;
    /// <summary>Invalid usage, configuration or input files.</summary>
    public const int UsageError = 2;
}

/// <summary>The <c>bdt</c> command-line application. Runs in-process so it can be tested without spawning processes.</summary>
internal sealed class CliApp(TextWriter output, TextWriter error, string currentDirectory)
{
    private readonly Option<string?> _config = new("--config", "-c") { Description = $"Path to the configuration file (default: ./{BdtConfig.FileName} when present)." };
    private readonly Option<string?> _themes = new("--themes") { Description = "Theme manifest (*.themes.json); overrides the configuration." };
    private readonly Option<ReportFormat> _format = new("--format", "-f") { Description = "Output format: text, json, github or sarif.", DefaultValueFactory = _ => ReportFormat.Text };
    private readonly Option<bool> _verbose = new("--verbose", "-v") { Description = "Also print informational diagnostics." };
    private readonly Option<string?> _report = new("--report") { Description = "Also write a JSON report to this file." };
    private readonly Option<DiagnosticSeverity?> _failOn = new("--fail-on") { Description = "Lowest severity that fails the command: none, info, warning or error." };
    private readonly Argument<string[]> _inputs = new("inputs") { Description = "Token files or globs; override the configuration.", Arity = ArgumentArity.ZeroOrMore };

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, string currentDirectory, CancellationToken cancellationToken = default)
        => new CliApp(output, error, currentDirectory).RunAsync(args, cancellationToken);

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var root = new RootCommand("bdt — validate, export and analyse DTCG 2025.10 design tokens and themes.");
        root.Subcommands.Add(ValidateCommand());
        root.Subcommands.Add(BuildCommand());
        root.Subcommands.Add(ExportCommand());
        root.Subcommands.Add(CoverageCommand());
        root.Subcommands.Add(DiffCommand());
        root.Subcommands.Add(CheckCommand());
        root.Subcommands.Add(RulesCommand());
        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var e in parse.Errors) await error.WriteLineAsync(e.Message);
            await error.WriteLineAsync("Run 'bdt --help' for usage.");
            return ExitCodes.UsageError;
        }
        try
        {
            return await parse.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Cancelled.");
            return ExitCodes.UsageError;
        }
    }

    // ---- validate -------------------------------------------------------------------------------------------

    private Command ValidateCommand()
    {
        var command = new Command("validate", "Validate token files or themes: structure, references, values, design-system rules and accessibility contrast.");
        command.Arguments.Add(_inputs);
        AddCommon(command);
        command.SetAction(async (parse, ct) =>
        {
            var (ws, code) = await LoadWorkspaceAsync(parse, ct);
            if (ws is null) return code;
            var (result, contrast) = Validate(ws, parse.GetValue(_failOn), ct);
            Report(parse, ws, result, w => WriteContrast(w, contrast));
            return result.Failed ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    internal static (DiagnosticResult Result, IReadOnlyList<ContrastResult> Contrast) Validate(Workspace ws, DiagnosticSeverity? failOn, CancellationToken cancellationToken)
    {
        var options = ws.Validation;
        var policy = failOn is { } f ? CopyPolicy(options.Policy, f) : options.Policy;
        options = options with { Policy = policy };
        var effective = new List<Diagnostic>();
        var suppressed = new List<Diagnostic>();
        var contrast = new List<ContrastResult>();
        var load = policy.Apply(ws.LoadDiagnostics);
        effective.AddRange(load.Diagnostics);
        suppressed.AddRange(load.Suppressed);
        var units = ws.Units(cancellationToken).ToList();
        foreach (var unit in units)
        {
            var report = TokenValidator.Validate(unit.Tokens, options, scope: unit.Scope, includeResolutionDiagnostics: unit.Scope is null);
            effective.AddRange(report.Result.Diagnostics);
            suppressed.AddRange(report.Result.Suppressed);
            if (unit.Scope is not null)
            {
                var composition = policy.Apply(unit.Diagnostics);
                effective.AddRange(composition.Diagnostics);
                suppressed.AddRange(composition.Suppressed);
            }
            contrast.AddRange(report.ContrastResults);
        }
        if (ws.Catalog is { } catalog)
        {
            var completeness = policy.Apply(ThemeCompletenessAnalyzer.Analyze(catalog).Diagnostics);
            effective.AddRange(completeness.Diagnostics);
            suppressed.AddRange(completeness.Suppressed);
        }
        return (new DiagnosticResult(Collapse(effective, units.Count), Collapse(suppressed, units.Count), policy.FailOn), contrast);
    }

    private static DiagnosticPolicy CopyPolicy(DiagnosticPolicy p, DiagnosticSeverity failOn)
        => new() { SeverityOverrides = p.SeverityOverrides, Suppressions = p.Suppressions, FailOn = failOn };

    /// <summary>
    /// The same finding in several theme combinations is reported once; its scope lists the combinations
    /// (or says it applies to all of them).
    /// </summary>
    internal static IReadOnlyList<Diagnostic> Collapse(IEnumerable<Diagnostic> diagnostics, int unitCount)
    {
        var result = new List<Diagnostic>();
        foreach (var group in diagnostics.GroupBy(static d => (d.RuleId, d.Severity, d.TokenPath, d.Message, d.Location?.ToString(), d.Component, d.IsSuppressed)))
        {
            var first = group.First();
            var scopes = group.Select(static d => d.Scope).Where(static s => s is not null).Distinct().Order(StringComparer.Ordinal).ToList();
            var scope = scopes.Count switch
            {
                0 => null,
                1 => scopes[0],
                _ when scopes.Count == unitCount && unitCount > 1 => "all theme combinations",
                <= 3 => string.Join("; ", scopes),
                _ => $"{scopes.Count} of {unitCount} theme combinations, e.g. {scopes[0]}",
            };
            result.Add(first.WithScope(scope));
        }
        result.Sort(Diagnostic.Comparer);
        return result;
    }

    private static void WriteContrast(Utf8JsonWriter w, IReadOnlyList<ContrastResult> results)
    {
        w.WriteStartArray("contrast");
        foreach (var r in results.OrderBy(static r => r.Scope, StringComparer.Ordinal).ThenBy(static r => r.Foreground, StringComparer.Ordinal).ThenBy(static r => r.Background, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("foreground", r.Foreground);
            w.WriteString("background", r.Background);
            w.WriteString("kind", r.Kind.ToString());
            w.WriteString("level", r.Level.ToString());
            if (r.Ratio is { } ratio) w.WriteNumber("ratio", Math.Floor(ratio * 100) / 100);
            w.WriteNumber("required", r.Required);
            if (r.Passed is { } passed) w.WriteBoolean("passed", passed);
            else w.WriteString("indeterminate", r.IndeterminateReason);
            if (r.Scope is not null) w.WriteString("scope", r.Scope);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ---- build ----------------------------------------------------------------------------------------------

    private Command BuildCommand()
    {
        var command = new Command("build", "Resolve tokens (one theme selection) and write a self-contained DTCG 2025.10 JSON file.");
        var outOption = new Option<string?>("--out", "-o") { Description = "Output file (default: standard output)." };
        var theme = new Option<string?>("--theme") { Description = "Theme id (with --themes or a configured manifest)." };
        var axes = new Option<string[]>("--axis") { Description = "Axis value, e.g. --axis mode=dark (repeatable).", AllowMultipleArgumentsPerToken = false };
        var preserve = new Option<bool>("--preserve-references") { Description = "Keep aliases instead of resolving values." };
        command.Arguments.Add(_inputs);
        command.Options.Add(_config);
        command.Options.Add(_themes);
        command.Options.Add(outOption);
        command.Options.Add(theme);
        command.Options.Add(axes);
        command.Options.Add(preserve);
        command.SetAction(async (parse, ct) =>
        {
            var (ws, code) = await LoadWorkspaceAsync(parse, ct);
            if (ws is null) return code;
            if (!TryParseAxes(parse.GetValue(axes), out var axisValues)) return ExitCodes.UsageError;
            var tokens = ws.Primary(parse.GetValue(theme), axisValues);
            if (tokens is null) { await error.WriteLineAsync("No tokens to build."); return ExitCodes.UsageError; }
            var json = DtcgWriter.Write(tokens, new DtcgWriteOptions { PreserveReferences = parse.GetValue(preserve) });
            var errors = tokens.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
            foreach (var d in errors) await error.WriteLineAsync(d.ToString());
            if (parse.GetValue(outOption) is { } path) await WriteFileAsync(Resolve(path), json, ct);
            else await output.WriteAsync(json);
            return errors.Count > 0 ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    // ---- export ---------------------------------------------------------------------------------------------

    private Command ExportCommand()
    {
        var command = new Command("export", "Generate configured outputs (CSS, theme CSS, Tailwind, Bootstrap, Fluent, DTCG JSON), or one ad-hoc output.");
        var formatOption = new Option<string?>("--to", "-t") { Description = "Ad-hoc output format (css, theme-css, dtcg-json, tailwind, bootstrap-scss, bootstrap-css, fluent)." };
        var outOption = new Option<string?>("--out", "-o") { Description = "Ad-hoc output path (default: standard output)." };
        var settings = new Option<string[]>("--set") { Description = "Ad-hoc exporter setting, e.g. --set prefix=ds (repeatable)." };
        var check = new Option<bool>("--check") { Description = "Do not write; fail when files on disk differ from what would be generated." };
        command.Arguments.Add(_inputs);
        AddCommon(command);
        command.Options.Add(formatOption);
        command.Options.Add(outOption);
        command.Options.Add(settings);
        command.Options.Add(check);
        command.SetAction(async (parse, ct) =>
        {
            var (ws, code) = await LoadWorkspaceAsync(parse, ct);
            if (ws is null) return code;
            IReadOnlyList<OutputConfig> outputs;
            if (parse.GetValue(formatOption) is { } adHoc)
            {
                if (!TryParseAxes(parse.GetValue(settings), out var settingValues)) return ExitCodes.UsageError;
                outputs = [new OutputConfig(adHoc, parse.GetValue(outOption) ?? "-", settingValues, null, null, new Dictionary<string, string>(), null)];
            }
            else outputs = ws.Config.Outputs;
            if (outputs.Count == 0) { await error.WriteLineAsync("No outputs configured. Add \"outputs\" to the configuration or use --to."); return ExitCodes.UsageError; }
            var (result, _) = await ExportAsync(ws, outputs, parse.GetValue(check), parse.GetValue(_failOn), ct);
            Report(parse, ws, result);
            return result.Diagnostics.Any(static d => d.Category == DiagnosticCatalog.ConfigurationCategory && d.RuleId != DiagnosticCatalog.OutputOutOfDate.Id && d.Severity == DiagnosticSeverity.Error)
                ? ExitCodes.UsageError
                : result.Failed ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    internal async Task<(DiagnosticResult Result, int Written)> ExportAsync(Workspace ws, IReadOnlyList<OutputConfig> outputs, bool check, DiagnosticSeverity? failOn, CancellationToken ct)
    {
        var bag = new DiagnosticBag();
        bag.AddRange(ws.LoadDiagnostics);
        var registry = Workspace.CreateRegistry();
        var written = 0;
        foreach (var o in outputs)
        {
            var (content, diagnostics) = ws.Generate(o, registry, ct);
            bag.AddRange(diagnostics);
            if (content is null) continue;
            if (o.Path == "-")
            {
                await output.WriteAsync(content);
                continue;
            }
            var path = ws.Config.Resolve(o.Path);
            if (check)
            {
                var existing = File.Exists(path) ? (await File.ReadAllTextAsync(path, ct)).ReplaceLineEndings("\n") : null;
                if (existing != content)
                {
                    bag.Report(DiagnosticCatalog.OutputOutOfDate, existing is null ? $"'{o.Path}' does not exist." : $"'{o.Path}' is out of date.",
                        location: o.Location, actual: o.Path, remediation: "Run 'bdt export' and commit the result.");
                }
                continue;
            }
            await WriteFileAsync(path, content, ct);
            written++;
            await error.WriteLineAsync($"wrote {o.Path} ({o.Format})");
        }
        var policy = failOn is { } f ? CopyPolicy(ws.Validation.Policy, f) : ws.Validation.Policy;
        return (policy.Apply(bag.Items), written);
    }

    // ---- coverage -------------------------------------------------------------------------------------------

    private Command CoverageCommand()
    {
        var command = new Command("coverage", "Report token usage in source files, unused tokens, undefined custom properties and component contracts.");
        var sources = new Option<string[]>("--sources") { Description = "Source globs (override the configuration), e.g. --sources \"src/**/*.razor\"." };
        var prefix = new Option<string?>("--prefix") { Description = "Custom-property prefix used by the generated CSS." };
        var min = new Option<double?>("--min") { Description = "Minimum token coverage percentage." };
        command.Arguments.Add(_inputs);
        AddCommon(command);
        command.Options.Add(sources);
        command.Options.Add(prefix);
        command.Options.Add(min);
        command.SetAction(async (parse, ct) =>
        {
            var (ws, code) = await LoadWorkspaceAsync(parse, ct);
            if (ws is null) return code;
            var (result, report) = await CoverageAsync(ws, parse.GetValue(sources), parse.GetValue(prefix), parse.GetValue(min), parse.GetValue(_failOn), ct);
            if (report is null) { Report(parse, ws, result); return ExitCodes.UsageError; }
            var format = parse.GetValue(_format);
            if (format == ReportFormat.Text)
            {
                await output.WriteLineAsync($"Token coverage: {report.UsedTokenCount}/{report.TokenCount} tokens used ({Workspace.Percent(report.CoveragePercent)}); {report.DirectlyUsedTokenCount} used directly, {report.ScannedFileCount} files scanned.");
                foreach (var c in report.Components) await output.WriteLineAsync($"  {c.Name}: {c.UsedTokens.Count} tokens{(c.MissingRequired.Count > 0 ? $", missing {string.Join(", ", c.MissingRequired)}" : "")}");
                await output.WriteLineAsync("Limitations:");
                foreach (var l in report.Limitations) await output.WriteLineAsync("  - " + l);
            }
            Report(parse, ws, result, w => WriteCoverage(w, report));
            return result.Failed ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    internal static async Task<(DiagnosticResult Result, CoverageReport? Report)> CoverageAsync(Workspace ws, string[]? sourceOverride, string? prefixOverride, double? minOverride,
        DiagnosticSeverity? failOn, CancellationToken ct)
    {
        var bag = new DiagnosticBag();
        bag.AddRange(ws.LoadDiagnostics.Where(static d => d.Category == DiagnosticCatalog.ConfigurationCategory));
        var config = ws.Config.Coverage;
        var patterns = sourceOverride is { Length: > 0 } ? sourceOverride : config?.Sources ?? [];
        var policy = failOn is { } f ? CopyPolicy(ws.Validation.Policy, f) : ws.Validation.Policy;
        if (patterns.Count == 0)
        {
            bag.Report(DiagnosticCatalog.InvalidConfiguration, "No coverage sources: configure \"coverage.sources\" or pass --sources.");
            return (policy.Apply(bag.Items), null);
        }
        var tokens = ws.Primary();
        if (tokens is null) { bag.Report(DiagnosticCatalog.InvalidConfiguration, "No tokens available."); return (policy.Apply(bag.Items), null); }
        var files = ws.Config.Expand(patterns, config?.Exclude, bag);
        var texts = new List<SourceText>();
        foreach (var file in files) texts.Add(new SourceText(Workspace.Relative(ws.Config, file), await File.ReadAllTextAsync(file, ct)));
        var manifests = new List<ComponentManifest>();
        foreach (var m in ws.Config.Expand(config?.Manifests ?? [], null, bag))
        {
            var parsed = await SourceJsonParser.ParseFileAsync(m, cancellationToken: ct);
            bag.AddRange(parsed.Diagnostics);
            if (parsed.Root is { } root) manifests.AddRange(ComponentManifest.ReadMany(root, bag));
        }
        var prefix = prefixOverride ?? config?.Prefix ?? ws.Config.Outputs.Select(o => o.Settings.GetValueOrDefault("prefix")).FirstOrDefault(p => p is not null) ?? "";
        var report = TokenCoverageAnalyzer.Analyze(tokens, texts, new CoverageOptions
        {
            Css = CssExportOptions.Default with { Prefix = prefix },
            Components = manifests,
            EntryPoints = config?.EntryPoints ?? [],
        });
        bag.AddRange(report.Diagnostics);
        var min = minOverride ?? config?.MinimumPercent;
        var result = policy.Apply(bag.Items);
        if (min is { } threshold && report.CoveragePercent < threshold)
        {
            var below = new Diagnostic(DiagnosticCatalog.InvalidConfiguration.Id, DiagnosticSeverity.Error,
                $"Token coverage {Workspace.Percent(report.CoveragePercent)} is below the required {Workspace.Percent(threshold)}.", expected: $">= {threshold}%",
                actual: $"{report.CoveragePercent}%", remediation: "Remove unused tokens, add usages, or declare public tokens as coverage entry points.", category: DiagnosticCatalog.CoverageCategory);
            result = new DiagnosticResult([.. result.Diagnostics, below], result.Suppressed, result.FailOn == DiagnosticSeverity.None ? DiagnosticSeverity.Error : result.FailOn);
        }
        return (result, report);
    }

    private static void WriteCoverage(Utf8JsonWriter w, CoverageReport report)
    {
        w.WriteStartObject("coverage");
        w.WriteNumber("tokens", report.TokenCount);
        w.WriteNumber("used", report.UsedTokenCount);
        w.WriteNumber("usedDirectly", report.DirectlyUsedTokenCount);
        w.WriteNumber("percent", report.CoveragePercent);
        w.WriteNumber("files", report.ScannedFileCount);
        w.WriteStartArray("unused");
        foreach (var u in report.UnusedTokens) w.WriteStringValue(u);
        w.WriteEndArray();
        w.WriteStartArray("undefinedVariables");
        foreach (var u in report.UndefinedVariables) w.WriteStringValue(u);
        w.WriteEndArray();
        w.WriteStartArray("usages");
        foreach (var u in report.Usages)
        {
            w.WriteStartObject();
            w.WriteString("token", u.Token.ToString());
            w.WriteString("file", u.File);
            w.WriteNumber("line", u.Line);
            w.WriteString("variable", u.Variable);
            if (u.Component is not null) w.WriteString("component", u.Component);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("components");
        foreach (var c in report.Components)
        {
            w.WriteStartObject();
            w.WriteString("name", c.Name);
            w.WriteStartArray("tokens");
            foreach (var t in c.UsedTokens) w.WriteStringValue(t);
            w.WriteEndArray();
            w.WriteStartArray("missingRequired");
            foreach (var t in c.MissingRequired) w.WriteStringValue(t);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("limitations");
        foreach (var l in report.Limitations) w.WriteStringValue(l);
        w.WriteEndArray();
        w.WriteEndObject();
    }

    // ---- diff -----------------------------------------------------------------------------------------------

    private Command DiffCommand()
    {
        var command = new Command("diff", "Compare two token files (resolved values, types, references, deprecations).");
        var before = new Argument<string>("before") { Description = "Token file before the change." };
        var after = new Argument<string>("after") { Description = "Token file after the change." };
        var failOnBreaking = new Option<bool>("--fail-on-breaking") { Description = "Exit with 1 when tokens were removed or changed type." };
        command.Arguments.Add(before);
        command.Arguments.Add(after);
        command.Options.Add(_format);
        command.Options.Add(failOnBreaking);
        command.SetAction(async (parse, ct) =>
        {
            var a = await ResolveFileAsync(parse.GetValue(before)!, ct);
            var b = await ResolveFileAsync(parse.GetValue(after)!, ct);
            if (a is null || b is null) return ExitCodes.UsageError;
            var diff = TokenDiff.Compare(a, b);
            if (parse.GetValue(_format) == ReportFormat.Json)
            {
                await output.WriteLineAsync(Reporting.Json(w =>
                {
                    w.WriteStartObject();
                    w.WriteBoolean("breaking", diff.HasBreakingChanges);
                    w.WriteStartArray("changes");
                    foreach (var c in diff.Changes)
                    {
                        w.WriteStartObject();
                        w.WriteString("token", c.Path);
                        w.WriteString("kind", c.Kind.ToString());
                        if (c.Before is not null) w.WriteString("before", c.Before);
                        if (c.After is not null) w.WriteString("after", c.After);
                        w.WriteBoolean("breaking", c.IsBreaking);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }));
            }
            else
            {
                foreach (var c in diff.Changes)
                {
                    var marker = c.Kind switch { TokenChangeKind.Added => "+", TokenChangeKind.Removed => "-", _ => "~" };
                    await output.WriteLineAsync($"{marker} {c.Path} {c.Kind}{(c.IsBreaking ? " (breaking)" : "")}{(c.Before is null ? "" : $"\n    before: {c.Before}")}{(c.After is null ? "" : $"\n    after:  {c.After}")}");
                }
                await output.WriteLineAsync($"{diff.Changes.Count} change(s); {diff.Changes.Count(static c => c.IsBreaking)} breaking.");
            }
            return parse.GetValue(failOnBreaking) && diff.HasBreakingChanges ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    private async Task<ResolvedTokenSet?> ResolveFileAsync(string path, CancellationToken ct)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) { await error.WriteLineAsync($"File '{path}' was not found."); return null; }
        var document = await DtcgReader.ReadFileAsync(full, new JsonParseOptions { SourceName = path }, ct);
        if (document.Diagnostics.Any(static d => d.RuleId is "BDT1001" or "BDT1003" or "BDT1004" or "BDT1005"))
        {
            foreach (var d in document.Diagnostics) await error.WriteLineAsync(d.ToString());
            return null;
        }
        return TokenResolver.Resolve(document, cancellationToken: ct);
    }

    // ---- check ----------------------------------------------------------------------------------------------

    private Command CheckCommand()
    {
        var command = new Command("check", "Run every configured quality gate: validation, up-to-date outputs and (if configured) coverage.");
        AddCommon(command);
        command.SetAction(async (parse, ct) =>
        {
            var (ws, code) = await LoadWorkspaceAsync(parse, ct);
            if (ws is null) return code;
            var failOn = parse.GetValue(_failOn);
            var (validation, contrast) = Validate(ws, failOn, ct);
            var all = new List<Diagnostic>(validation.Diagnostics);
            var suppressed = new List<Diagnostic>(validation.Suppressed);
            var failed = validation.Failed;
            if (ws.Config.Outputs.Count > 0)
            {
                var (exported, _) = await ExportAsync(ws, ws.Config.Outputs, check: true, failOn, ct);
                all.AddRange(exported.Diagnostics.Where(d => !validation.Diagnostics.Any(v => v.Message == d.Message && v.RuleId == d.RuleId)));
                failed |= exported.Failed;
            }
            CoverageReport? coverageReport = null;
            if (ws.Config.Coverage is not null)
            {
                var (coverage, report) = await CoverageAsync(ws, null, null, null, failOn, ct);
                coverageReport = report;
                all.AddRange(coverage.Diagnostics.Where(d => d.Category is DiagnosticCatalog.CoverageCategory or DiagnosticCatalog.ConfigurationCategory));
                failed |= coverage.Failed;
            }
            var policy = failOn is { } f ? f : ws.Validation.Policy.FailOn;
            var collapsed = Collapse(all, 1);
            // Report the configured threshold; fall back only when the combined diagnostics alone would disagree with the
            // gates' verdict (for example a stale output under failOn "none").
            var combined = new DiagnosticResult(collapsed, suppressed, policy);
            if (combined.Failed != failed)
                combined = new DiagnosticResult(collapsed, suppressed, failed ? (policy == DiagnosticSeverity.None ? DiagnosticSeverity.Error : policy) : DiagnosticSeverity.None);
            Report(parse, ws, combined, w =>
            {
                WriteContrast(w, contrast);
                if (coverageReport is not null) WriteCoverage(w, coverageReport);
            });
            return failed ? ExitCodes.GateFailed : ExitCodes.Success;
        });
        return command;
    }

    // ---- rules ----------------------------------------------------------------------------------------------

    private Command RulesCommand()
    {
        var command = new Command("rules", "List every diagnostic rule with its identifier, category and default severity.");
        var markdown = new Option<bool>("--markdown") { Description = "Print a Markdown table (used to generate the documentation)." };
        command.Options.Add(markdown);
        command.Options.Add(_format);
        command.SetAction(async (parse, ct) =>
        {
            if (parse.GetValue(_format) == ReportFormat.Json)
            {
                await output.WriteLineAsync(Reporting.Json(w =>
                {
                    w.WriteStartArray();
                    foreach (var d in DiagnosticCatalog.All)
                    {
                        w.WriteStartObject();
                        w.WriteString("id", d.Id);
                        w.WriteString("title", d.Title);
                        w.WriteString("category", d.Category);
                        w.WriteString("defaultSeverity", d.DefaultSeverity.ToString().ToLowerInvariant());
                        w.WriteBoolean("specRequirement", d.IsSpecRequirement);
                        w.WriteString("description", d.Description);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }));
            }
            else if (parse.GetValue(markdown))
            {
                await output.WriteLineAsync("| Rule | Title | Category | Default | DTCG requirement | Description |");
                await output.WriteLineAsync("|---|---|---|---|---|---|");
                foreach (var d in DiagnosticCatalog.All)
                    await output.WriteLineAsync($"| {d.Id} | {d.Title} | {d.Category} | {d.DefaultSeverity.ToString().ToLowerInvariant()} | {(d.IsSpecRequirement ? "yes" : "")} | {d.Description.Replace("|", "\\|")} |");
            }
            else
            {
                foreach (var d in DiagnosticCatalog.All) await output.WriteLineAsync($"{d.Id}  {d.DefaultSeverity.ToString().ToLowerInvariant(),-7}  {d.Category,-13}  {d.Title}");
            }
            return ExitCodes.Success;
        });
        return command;
    }

    // ---- shared ---------------------------------------------------------------------------------------------

    private void AddCommon(Command command)
    {
        command.Options.Add(_config);
        command.Options.Add(_themes);
        command.Options.Add(_format);
        command.Options.Add(_verbose);
        command.Options.Add(_report);
        command.Options.Add(_failOn);
    }

    private string Resolve(string path) => Path.GetFullPath(Path.Combine(currentDirectory, path));

    private async Task<(Workspace? Workspace, int ExitCode)> LoadWorkspaceAsync(ParseResult parse, CancellationToken ct)
    {
        BdtConfig? config;
        var configPath = parse.GetValue(_config);
        var defaultPath = Path.Combine(currentDirectory, BdtConfig.FileName);
        if (configPath is not null || File.Exists(defaultPath))
        {
            var (loaded, diagnostics) = await BdtConfig.LoadAsync(configPath is null ? defaultPath : Resolve(configPath), ct);
            if (loaded is null)
            {
                foreach (var d in diagnostics) await error.WriteLineAsync(d.ToString());
                return (null, ExitCodes.UsageError);
            }
            config = loaded;
        }
        else
        {
            config = new BdtConfig { BaseDirectory = currentDirectory };
        }
        var inputs = parse.GetResult(_inputs) is not null ? parse.GetValue(_inputs) ?? [] : [];
        var themes = parse.GetValue(_themes);
        if (inputs.Length > 0)
        {
            config = new BdtConfig
            {
                BaseDirectory = currentDirectory, Tokens = inputs, Themes = null, Parse = config.Parse, AllowLegacyValueFormats = config.AllowLegacyValueFormats,
                AdditionalDimensionUnits = config.AdditionalDimensionUnits, ValidationNode = config.ValidationNode,
                ValidationFile = config.ValidationFile is null ? null : config.Resolve(config.ValidationFile), Policy = config.Policy, Outputs = config.Outputs, Coverage = config.Coverage,
            };
        }
        if (themes is not null)
        {
            config = new BdtConfig
            {
                BaseDirectory = config.BaseDirectory, Tokens = [], Themes = Path.GetFullPath(Path.Combine(currentDirectory, themes)), Parse = config.Parse,
                AllowLegacyValueFormats = config.AllowLegacyValueFormats, AdditionalDimensionUnits = config.AdditionalDimensionUnits, ValidationNode = config.ValidationNode,
                ValidationFile = config.ValidationFile, Policy = config.Policy, Outputs = config.Outputs, Coverage = config.Coverage,
            };
        }
        if (config.Tokens.Count == 0 && config.Themes is null)
        {
            await error.WriteLineAsync($"No tokens to process. Pass token files, use --themes, or create {BdtConfig.FileName}.");
            return (null, ExitCodes.UsageError);
        }
        var ws = await Workspace.LoadAsync(config, ct);
        var configErrors = ws.LoadDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error && d.Category == DiagnosticCatalog.ConfigurationCategory).ToList();
        if (configErrors.Count > 0)
        {
            foreach (var d in configErrors) await error.WriteLineAsync(d.ToString());
            return (null, ExitCodes.UsageError);
        }
        return (ws, ExitCodes.Success);
    }

    private void Report(ParseResult parse, Workspace ws, DiagnosticResult result, Action<Utf8JsonWriter>? extra = null)
    {
        Reporting.Write(output, parse.GetValue(_format), result, ws.Config.BaseDirectory, parse.GetValue(_verbose), extra);
        if (parse.GetValue(_report) is { } reportPath)
        {
            using var writer = new StringWriter();
            Reporting.WriteJson(writer, result, extra);
            var full = Resolve(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, writer.ToString().ReplaceLineEndings("\n"));
        }
    }

    private bool TryParseAxes(string[]? values, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in values ?? [])
        {
            var eq = v.IndexOf('=');
            if (eq <= 0)
            {
                error.WriteLine($"Expected name=value, got '{v}'.");
                return false;
            }
            result[v[..eq]] = v[(eq + 1)..];
        }
        return true;
    }

    private static async Task WriteFileAsync(string path, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content.ReplaceLineEndings("\n"), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }
}
