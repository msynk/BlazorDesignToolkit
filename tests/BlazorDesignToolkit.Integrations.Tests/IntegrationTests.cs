using System.Diagnostics;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Bootstrap;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Fluent;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Tailwind;

namespace BlazorDesignToolkit.Integrations.Tests;

internal static class Fixture
{
    private static readonly Lazy<ThemeCatalog> s_catalog = new(() =>
        ThemeManifest.LoadAsync(Path.Combine(AppContext.BaseDirectory, "SampleTokens", "aurora.themes.json")).GetAwaiter().GetResult().Catalog!);

    public static ResolvedTokenSet Light => s_catalog.Value.Resolve(s_catalog.Value.DefaultSelection).Tokens;

    public static readonly CssExportOptions Css = CssExportOptions.Default with { Prefix = "ds" };

    public static TailwindMapping[] TailwindMappings =>
    [
        new("color.surface.*", "color", "color"),
        new("color.text.*", "color", "color"),
        new("color.border.*", "color", "color"),
        new("radius.*", "radius"),
        new("shadow.*", "shadow"),
        new("typography.*", "text"),
        new("font.family.*", "font"),
        new("easing.*", "ease"),
    ];

    public static TargetMapping[] BootstrapMappings =>
    [
        new("primary", "color.surface.accent"),
        new("danger", "color.surface.danger"),
        new("body-bg", "color.surface.default"),
        new("body-color", "color.text.default"),
        new("border-color", "color.border.default"),
        new("link-color", "color.text.link"),
        new("border-radius", "radius.md"),
        new("font-family-sans-serif", "font.family.sans"),
        new("font-weight-bold", "font.weight.bold"),
        new("line-height-base", "font.line-height.normal"),
        new("box-shadow", "shadow.raised"),
    ];

    public static string? ToolsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        var tools = dir is null ? null : Path.Combine(dir.FullName, "tests", "integration-tools");
        return tools is not null && Directory.Exists(Path.Combine(tools, "node_modules")) ? tools : null;
    }

    /// <summary>Runs a Node-based compiler; skips the test explicitly (never passes silently) when the pinned tools are not installed.</summary>
    public static (int ExitCode, string Output) RunNode(string tools, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("node") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2))) { process.Kill(true); throw new TimeoutException("Compiler timed out."); }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    public static string RequireTools()
    {
        var tools = ToolsDirectory();
        if (tools is null)
        {
            Assert.Skip("Pinned integration compilers are not installed. Run 'npm ci' in tests/integration-tools (CI does this) to execute this test.");
        }
        return tools!;
    }

    public static string WorkDirectory(string tools, string name)
    {
        var dir = Path.Combine(tools, ".work", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class TailwindTests
{
    [Fact]
    public void Generates_type_checked_theme_block_referencing_runtime_variables()
    {
        var output = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions { Mappings = Fixture.TailwindMappings, Css = Fixture.Css });
        Assert.Empty(output.Diagnostics);
        Assert.Contains("@theme inline {", output.Content);
        Assert.Contains("--color-surface-default: var(--ds-color-surface-default);", output.Content);
        Assert.Contains("--radius-md: var(--ds-radius-md);", output.Content);
        Assert.Contains("--text-body: var(--ds-typography-body-font-size);", output.Content);
        Assert.Contains("--text-body--line-height: var(--ds-typography-body-line-height);", output.Content);
        Assert.Contains("--font-sans: var(--ds-font-family-sans);", output.Content);
        Assert.Contains("--ease-standard: var(--ds-easing-standard);", output.Content);
    }

    [Fact]
    public void Literal_mode_emits_values()
    {
        var output = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions
        {
            Mappings = [new("radius.*", "radius"), new("typography.*", "text")],
            Values = IntegrationValueMode.Literal,
            ResetMappedNamespaces = true,
        });
        Assert.Contains("@theme {", output.Content);
        Assert.Contains("--radius-*: initial;", output.Content);
        Assert.Contains("--radius-md: 0.5rem;", output.Content);
        Assert.Contains("--text-body--line-height: 1.5;", output.Content);
    }

    [Fact]
    public void Type_mismatches_and_unknown_namespaces_are_reported()
    {
        var output = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions
        {
            Mappings = [new("space.*", "radius"), new("color.text.*", "colour"), new("duration.*", "ease")],
            Css = Fixture.Css,
        });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidMapping.Id && d.Actual == "colour");
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.MappingTypeMismatch.Id && d.TokenPath == "duration.fast");
        Assert.DoesNotContain("--ease-fast", output.Content);
    }

    [Fact]
    public void Reference_mode_requires_a_prefix_to_avoid_self_references()
    {
        var output = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions { Mappings = Fixture.TailwindMappings });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidConfiguration.Id);
        Assert.Equal("", output.Content);
    }

    [Fact]
    public void Drop_shadow_rejects_spread_and_inset()
    {
        var output = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions { Mappings = [new("shadow.*", "drop-shadow")], Values = IntegrationValueMode.Literal });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnsupportedConversion.Id && d.TokenPath == "shadow.overlay");
        Assert.Contains("--drop-shadow-raised: 0px 1px 3px", output.Content);
    }

    [Fact]
    public void Generated_theme_compiles_with_tailwind_v4()
    {
        var tools = Fixture.RequireTools();
        var work = Fixture.WorkDirectory(tools, "tailwind");
        var theme = TailwindThemeExporter.Export(Fixture.Light, new TailwindExportOptions { Mappings = Fixture.TailwindMappings, Css = Fixture.Css, IncludeHeader = false });
        File.WriteAllText(Path.Combine(work, "input.css"), "@import \"tailwindcss\";\n@source \"./page.html\";\n" + theme.Content);
        File.WriteAllText(Path.Combine(work, "page.html"), "<div class=\"bg-surface-default text-text-default rounded-md shadow-raised text-body font-sans ease-standard border-border-default\"></div>");
        var (exit, log) = Fixture.RunNode(tools, work, Path.Combine(tools, "node_modules", "@tailwindcss", "cli", "dist", "index.mjs"), "-i", "input.css", "-o", "output.css");
        Assert.True(exit == 0, log);
        var css = File.ReadAllText(Path.Combine(work, "output.css"));
        Assert.Contains("background-color: var(--ds-color-surface-default)", css);
        Assert.Contains("border-radius: var(--ds-radius-md)", css);
        Assert.Contains("font-size: var(--ds-typography-body-font-size)", css);
        Assert.Contains("line-height: var(--tw-leading, var(--ds-typography-body-line-height))", css);
        Assert.Contains("font-family: var(--ds-font-family-sans)", css);
        Directory.Delete(work, true);
    }
}

public class BootstrapTests
{
    [Fact]
    public void Scss_output_is_type_checked_and_srgb()
    {
        var output = BootstrapExporter.Export(Fixture.Light, new BootstrapExportOptions { Mappings = Fixture.BootstrapMappings });
        Assert.Empty(output.Diagnostics);
        Assert.Contains("$primary: #2d6dca;", output.Content);
        Assert.Contains("$body-bg: #ffffff;", output.Content);
        Assert.Contains("$font-family-sans-serif: \"Inter\", \"Segoe UI\", \"Roboto\", system-ui, sans-serif;", output.Content);
        Assert.Contains("$line-height-base: 1.5;", output.Content);
        Assert.Contains("$box-shadow: 0px 1px 3px 0px #0000001f;", output.Content);
    }

    [Fact]
    public void Runtime_css_references_tokens_and_emits_rgb_companions()
    {
        var output = BootstrapExporter.Export(Fixture.Light, new BootstrapExportOptions
        {
            Mappings = Fixture.BootstrapMappings,
            Output = BootstrapOutput.Css,
            Css = Fixture.Css,
        });
        Assert.Contains("--bs-primary: var(--ds-color-surface-accent);", output.Content);
        Assert.Contains("--bs-primary-rgb: 45, 109, 202;", output.Content);
        Assert.Contains("--bs-body-font-family: var(--ds-font-family-sans)", output.Content.Replace("--bs-font-sans-serif", "--bs-body-font-family"));
        // Sass-only variables are reported for runtime output instead of emitting a variable Bootstrap never reads.
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnsupportedConversion.Id && d.Message.Contains("font-weight-bold"));
    }

    [Fact]
    public void Invalid_mappings_are_reported()
    {
        var output = BootstrapExporter.Export(Fixture.Light, new BootstrapExportOptions
        {
            Mappings = [new("primery", "color.surface.accent"), new("primary", "radius.md"), new("secondary", "color.nope"), new("body-bg", "color.overlay.scrim")],
        });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidMapping.Id && d.Actual == "primery");
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.MappingTypeMismatch.Id && d.TokenPath == "radius.md");
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidMapping.Id && d.TokenPath == "color.nope");
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnsupportedConversion.Id && d.TokenPath == "color.overlay.scrim");
    }

    [Fact]
    public void Generated_variables_compile_with_bootstrap_sass()
    {
        var tools = Fixture.RequireTools();
        var work = Fixture.WorkDirectory(tools, "bootstrap");
        var scss = BootstrapExporter.Export(Fixture.Light, new BootstrapExportOptions { Mappings = Fixture.BootstrapMappings });
        File.WriteAllText(Path.Combine(work, "_tokens.scss"), scss.Content);
        File.WriteAllText(Path.Combine(work, "main.scss"), "@import \"tokens\";\n@import \"bootstrap/scss/bootstrap\";\n");
        var (exit, log) = Fixture.RunNode(tools, work, Path.Combine(tools, "node_modules", "sass", "sass.js"), "--load-path=" + Path.Combine(tools, "node_modules"),
            "--quiet-deps", "--silence-deprecation=import", "--silence-deprecation=global-builtin", "--silence-deprecation=color-functions", "main.scss", "bootstrap.css");
        Assert.True(exit == 0, log);
        var css = File.ReadAllText(Path.Combine(work, "bootstrap.css"));
        Assert.Contains("--bs-primary: #2d6dca;", css);
        Assert.Contains("--bs-btn-bg: #2d6dca;", css);
        Assert.Contains("--bs-body-bg: #ffffff;", css);
        Directory.Delete(work, true);
    }
}

public class FluentTests
{
    private static readonly TargetMapping[] s_mappings =
    [
        new("colorNeutralForeground1", "color.text.default"),
        new("colorNeutralBackground1", "color.surface.default"),
        new("colorBrandBackground", "color.surface.accent"),
        new("colorStrokeFocus2", "color.focus.ring"),
        new("borderRadiusMedium", "radius.md"),
        new("fontFamilyBase", "font.family.sans"),
        new("fontWeightSemibold", "font.weight.semibold"),
        new("durationNormal", "duration.normal"),
        new("curveEasyEase", "easing.standard"),
        new("shadow4", "shadow.raised"),
    ];

    [Fact]
    public void Maps_semantic_tokens_to_fluent_custom_properties()
    {
        var output = FluentExporter.Export(Fixture.Light, new FluentExportOptions { Mappings = s_mappings, Css = Fixture.Css });
        Assert.Empty(output.Diagnostics);
        Assert.Contains("--colorBrandBackground: var(--ds-color-surface-accent);", output.Content);
        Assert.Contains("--shadow4: var(--ds-shadow-raised);", output.Content);
    }

    [Fact]
    public void Literal_mode_writes_milliseconds_for_durations()
    {
        var output = FluentExporter.Export(Fixture.Light, new FluentExportOptions { Mappings = s_mappings, Values = IntegrationValueMode.Literal });
        Assert.Contains("--durationNormal: 200ms;", output.Content);
        Assert.Contains("--curveEasyEase: cubic-bezier(0.2, 0, 0, 1);", output.Content);
    }

    [Fact]
    public void Unitless_line_heights_and_unknown_names_are_rejected()
    {
        var output = FluentExporter.Export(Fixture.Light, new FluentExportOptions
        {
            Mappings = [new("lineHeightBase300", "font.line-height.normal"), new("colorbrandbackground", "color.surface.accent")],
            Css = Fixture.Css,
        });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.MappingTypeMismatch.Id && d.Expected == "dimension");
        var unknown = output.Diagnostics.Single(d => d.RuleId == DiagnosticCatalog.InvalidMapping.Id);
        Assert.Contains("colorBrandBackground", unknown.Remediation);
    }
}
