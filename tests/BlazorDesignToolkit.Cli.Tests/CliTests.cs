using System.Text.Json;

namespace BlazorDesignToolkit.Cli.Tests;

/// <summary>Runs the CLI in-process against a private copy of the sample workspace.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string _root;

    public CliTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bdt-cli-" + Guid.NewGuid().ToString("N"));
        Copy(Path.Combine(AppContext.BaseDirectory, "demo"), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private Task<(int Exit, string Out, string Err)> Run(params string[] args) => RunIn(_root, args);

    /// <summary>Runs in a directory without bdt.config.json, so only command-line inputs apply.</summary>
    private Task<(int Exit, string Out, string Err)> RunAdHoc(params string[] args)
    {
        var dir = Path.Combine(_root, "adhoc");
        Directory.CreateDirectory(dir);
        return RunIn(dir, args);
    }

    private static async Task<(int Exit, string Out, string Err)> RunIn(string directory, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(args, output, error, directory, TestContext.Current.CancellationToken);
        return (exit, output.ToString(), error.ToString());
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Validate_sample_configuration_passes()
    {
        var (exit, output, _) = await Run("validate");
        Assert.Equal(0, exit);
        Assert.Contains("0 error(s), 0 warning(s)", output);
        Assert.Contains("PASSED", output);
    }

    [Fact]
    public async Task Remote_sources_are_opt_in_and_validated()
    {
        var dir = Path.Combine(_root, "remote");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "t.tokens.json"), """{ "a": { "$type": "number", "$value": 1 } }""");
        File.WriteAllText(Path.Combine(dir, "bdt.config.json"), """{ "tokens": ["t.tokens.json"], "remote": { "allow": ["http://tokens.example.com/"] } }""");
        var (exit, output, error) = await RunIn(dir, ["validate"]);
        Assert.Equal(2, exit);
        Assert.Contains("must be an absolute https:// URL", output + error);

        File.WriteAllText(Path.Combine(dir, "bdt.config.json"), """{ "tokens": ["t.tokens.json"], "remote": { "allow": ["https://tokens.example.com/design/"], "timeoutSeconds": 10 } }""");
        Assert.Equal(0, (await RunIn(dir, ["validate"])).Exit);
    }

    [Fact]
    public async Task Validate_json_report_contains_contrast_results_and_summary()
    {
        var (exit, output, _) = await Run("validate", "--format", "json", "--report", "reports/validation.json");
        Assert.Equal(0, exit);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.False(json.GetProperty("summary").GetProperty("failed").GetBoolean());
        Assert.True(json.GetProperty("contrast").GetArrayLength() > 100);
        Assert.True(File.Exists(Path.Combine(_root, "reports", "validation.json")));
    }

    [Fact]
    public async Task Invalid_tokens_fail_with_exit_code_1_and_actionable_output()
    {
        Write("adhoc/bad.tokens.json", """{ "color": { "$type": "color", "a": { "$value": "{color.b}" }, "b": { "$value": "{color.a}" }, "c": { "$value": "#fff" } } }""");
        var (exit, output, _) = await RunAdHoc("validate", "bad.tokens.json");
        Assert.Equal(1, exit);
        Assert.Contains("BDT3002", output);
        Assert.Contains("color.a -> color.b -> color.a", output);
        Assert.Contains("bad.tokens.json(1,", output);
        Assert.Contains("FAILED", output);
    }

    [Fact]
    public async Task Fail_on_threshold_is_configurable()
    {
        Write("adhoc/warn.tokens.json", """{ "a": { "$type": "number", "$value": 1, "$future": true } }""");
        Assert.Equal(0, (await RunAdHoc("validate", "warn.tokens.json")).Exit);
        Assert.Equal(1, (await RunAdHoc("validate", "warn.tokens.json", "--fail-on", "warning")).Exit);
    }

    [Fact]
    public async Task Github_and_sarif_formats()
    {
        Write("adhoc/bad.tokens.json", """{ "a": { "$type": "number", "$value": "{missing}" } }""");
        var github = await RunAdHoc("validate", "bad.tokens.json", "--format", "github");
        Assert.StartsWith("::error file=bad.tokens.json,line=1,", github.Out);
        var sarif = JsonDocument.Parse((await RunAdHoc("validate", "bad.tokens.json", "--format", "sarif")).Out).RootElement;
        Assert.Equal("2.1.0", sarif.GetProperty("version").GetString());
        var result = sarif.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.Equal("BDT3001", result.GetProperty("ruleId").GetString());
        Assert.Equal("bad.tokens.json", result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task Usage_errors_return_exit_code_2()
    {
        Assert.Equal(2, (await Run("validate", "--no-such-option")).Exit);
        Assert.Equal(2, (await Run("validate", "--config", "missing.json")).Exit);
        Assert.Equal(2, (await Run("export", "--to", "nope", "tokens/foundation.tokens.json")).Exit);
        Write("broken.config.json", """{ "tokens": ["x.json"], "outputz": [] }""");
        var (exit, _, err) = await Run("validate", "--config", "broken.config.json");
        Assert.Equal(2, exit);
        Assert.Contains("outputz", err);
    }

    [Fact]
    public async Task Export_is_deterministic_and_check_detects_stale_outputs()
    {
        Assert.Equal(0, (await Run("export")).Exit);
        var first = File.ReadAllText(Path.Combine(_root, "generated", "aurora.themes.css"));
        Assert.Equal(0, (await Run("export")).Exit);
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, "generated", "aurora.themes.css")));
        Assert.Equal(0, (await Run("export", "--check")).Exit);

        File.AppendAllText(Path.Combine(_root, "generated", "aurora.tailwind.css"), "/* edited */");
        var (exit, output, _) = await Run("export", "--check");
        Assert.Equal(1, exit);
        Assert.Contains("BDT9004", output);
        Assert.Contains("aurora.tailwind.css", output);
    }

    [Fact]
    public async Task Committed_sample_outputs_are_up_to_date()
    {
        // The repository commits generated sample outputs; this fails when tokens change without regenerating them.
        var (exit, output, _) = await Run("export", "--check");
        Assert.True(exit == 0, output);
    }

    [Fact]
    public async Task Ad_hoc_export_writes_to_standard_output()
    {
        var (exit, output, _) = await Run("export", "tokens/foundation.tokens.json", "--to", "css", "--set", "prefix=x", "--set", "layer=base");
        Assert.Equal(0, exit);
        Assert.Contains("@layer base {", output);
        Assert.Contains("--x-color-blue-600: #2d6dca;", output);
    }

    [Fact]
    public async Task Build_writes_resolved_dtcg_json()
    {
        var (exit, _, _) = await Run("build", "--theme", "ember", "--axis", "mode=dark", "--out", "dist/ember-dark.tokens.json");
        Assert.Equal(0, exit);
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "dist", "ember-dark.tokens.json"))).RootElement;
        var accent = json.GetProperty("color").GetProperty("surface").GetProperty("accent");
        Assert.Equal("color", accent.GetProperty("$type").GetString());
        Assert.Equal("srgb", accent.GetProperty("$value").GetProperty("colorSpace").GetString());
    }

    [Fact]
    public async Task Coverage_reports_usage_and_enforces_minimum()
    {
        Write("src/Button.razor.css", ".b { color: var(--ds-color-text-default); background: var(--ds-color-surface-acent); }");
        var (exit, output, _) = await Run("coverage", "--sources", "src/**/*.css", "--format", "json");
        Assert.Equal(0, exit);
        var json = JsonDocument.Parse(output).RootElement.GetProperty("coverage");
        Assert.Equal(["--ds-color-surface-acent"], json.GetProperty("undefinedVariables").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains(json.GetProperty("usages").EnumerateArray(), u => u.GetProperty("component").GetString() == "Button");
        Assert.Equal(1, (await Run("coverage", "--sources", "src/**/*.css", "--min", "90")).Exit);
    }

    [Fact]
    public async Task Diff_reports_breaking_changes()
    {
        Write("adhoc/before.tokens.json", """{ "a": { "$type": "number", "$value": 1 }, "b": { "$type": "number", "$value": 2 } }""");
        Write("adhoc/after.tokens.json", """{ "a": { "$type": "dimension", "$value": { "value": 1, "unit": "px" } }, "c": { "$type": "number", "$value": 3 } }""");
        var (exit, output, _) = await RunAdHoc("diff", "before.tokens.json", "after.tokens.json", "--fail-on-breaking");
        Assert.Equal(1, exit);
        Assert.Contains("- b Removed (breaking)", output);
        Assert.Contains("+ c Added", output);
        Assert.Contains("~ a TypeChanged (breaking)", output);
        Assert.Equal(0, (await RunAdHoc("diff", "before.tokens.json", "after.tokens.json")).Exit);
    }

    [Fact]
    public async Task Check_runs_all_gates()
    {
        Write("Client/Sample.razor", "<div style=\"color: var(--ds-color-text-default)\"></div>");
        Write("Client/site.css", ".x { color: var(--ds-color-text-muted); }");
        Write("Server/App.razor", "<html></html>");
        var (exit, output, _) = await Run("check");
        Assert.True(exit == 0, output);
        Assert.Contains("PASSED", output);

        // The JSON report states the configured threshold, also when the gates pass.
        var (jsonExit, json, _) = await Run("check", "--format", "json");
        Assert.Equal(0, jsonExit);
        using var report = System.Text.Json.JsonDocument.Parse(json);
        var summary = report.RootElement.GetProperty("summary");
        Assert.Equal("error", summary.GetProperty("failOn").GetString());
        Assert.False(summary.GetProperty("failed").GetBoolean());
    }

    [Fact]
    public async Task Documented_rule_catalog_is_up_to_date()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var docs = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "validation.md")).ReplaceLineEndings("\n");
        var (_, table, _) = await Run("rules", "--markdown");
        Assert.True(docs.Contains(table.ReplaceLineEndings("\n").Trim(), StringComparison.Ordinal),
            "docs/validation.md is stale. Regenerate the table with: bdt rules --markdown");
    }

    [Fact]
    public async Task Rules_markdown_lists_every_rule()
    {
        var (exit, output, _) = await Run("rules", "--markdown");
        Assert.Equal(0, exit);
        foreach (var rule in BlazorDesignToolkit.Tokens.Diagnostics.DiagnosticCatalog.All) Assert.Contains($"| {rule.Id} |", output);
    }
}
