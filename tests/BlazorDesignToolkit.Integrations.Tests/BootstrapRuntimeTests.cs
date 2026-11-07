using System.Globalization;
using System.Text.RegularExpressions;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Bootstrap;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Integrations.Tests;

/// <summary>
/// The runtime Bootstrap variables must equal what Bootstrap's own Sass compiles for the same colors; the comparison runs
/// against a real Bootstrap 5.3.8 build for dark and light theme colors, in light and dark mode.
/// </summary>
public partial class BootstrapRuntimeTests
{
    private static readonly Dictionary<string, string> s_colors = new()
    {
        ["primary"] = "#2d6dca",   // dark enough for white text: hover shades
        ["success"] = "#1a7f37",
        ["warning"] = "#f5b800",   // light: black text, hover tints
        ["info"] = "#3dd5f3",
        ["danger"] = "#c62828",
        ["secondary"] = "#6c757d",
        ["light"] = "#f1f3f5",
        ["dark"] = "#1f2328",
    };

    private static ResolvedTokenSet Tokens()
    {
        var json = "{ \"c\": { \"$type\": \"color\", " + string.Join(", ", s_colors.Select(kv =>
        {
            var c = ParseColor(kv.Value);
            return $"\"{kv.Key}\": {{ \"$value\": {{ \"colorSpace\": \"srgb\", \"components\": [{F(c.R)}, {F(c.G)}, {F(c.B)}] }} }}";
        })) + " } }";
        return TokenResolver.Resolve(json);
        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static BootstrapExportOptions Options(IntegrationValueMode values = IntegrationValueMode.Literal) => new()
    {
        Mappings = s_colors.Keys.Select(k => new TargetMapping(k, "c." + k)).ToList(),
        Output = BootstrapOutput.Css,
        Values = values,
        Css = Fixture.Css,
    };

    [Fact]
    public void Derived_values_equal_bootstrap_sass_output()
    {
        var tools = Fixture.RequireTools();
        var work = Fixture.WorkDirectory(tools, "bootstrap-runtime");
        File.WriteAllText(Path.Combine(work, "main.scss"),
            string.Concat(s_colors.Select(kv => $"${kv.Key}: {kv.Value};\n")) + "@import \"bootstrap/scss/bootstrap\";\n");
        var (exit, log) = Fixture.RunNode(tools, work, Path.Combine(tools, "node_modules", "sass", "sass.js"), "--load-path=" + Path.Combine(tools, "node_modules"),
            "--quiet-deps", "--silence-deprecation=import", "--silence-deprecation=global-builtin", "--silence-deprecation=color-functions", "--no-source-map",
            "main.scss", "bootstrap.css");
        Assert.True(exit == 0, log);
        var compiled = File.ReadAllText(Path.Combine(work, "bootstrap.css"));

        var bag = new DiagnosticBag();
        var light = BootstrapRuntime.GetDeclarations(Tokens(), Options(), dark: false, bag).ToDictionary(d => d.Name, d => d.Value);
        var dark = BootstrapRuntime.GetDeclarations(Tokens(), Options(), dark: true, bag).ToDictionary(d => d.Name, d => d.Value);
        Assert.Empty(bag.Items);

        var checkedCount = 0;
        foreach (var name in s_colors.Keys)
        {
            var button = Block(compiled, $".btn-{name}");
            foreach (var state in new[] { "color", "hover-color", "hover-bg", "hover-border-color", "active-color", "active-bg", "active-border-color" })
            {
                AssertSameColor(button[$"--bs-btn-{state}"], light[$"--bdt-bs-btn-{name}-{state}"], $".btn-{name} --bs-btn-{state}");
                checkedCount++;
            }
            Assert.Equal(Rgb(button["--bs-btn-focus-shadow-rgb"]), Rgb(light[$"--bdt-bs-btn-{name}-focus-shadow-rgb"]));

            var outline = Block(compiled, $".btn-outline-{name}");
            AssertSameColor(outline["--bs-btn-hover-color"], light[$"--bdt-bs-{name}-contrast"], $".btn-outline-{name} hover color");

            AssertSameColor(Block(compiled, $".text-bg-{name}")["color"].Replace("!important", "").Trim(), light[$"--bdt-bs-{name}-contrast"], $".text-bg-{name}");
            var linkHover = LinkHoverRgb().Match(compiled.Replace(" ", "")[compiled.Replace(" ", "").IndexOf($".link-{name}:hover", StringComparison.Ordinal)..]);
            Assert.True(linkHover.Success, $".link-{name}:hover not found");
            Assert.Equal(Rgb(linkHover.Groups[1].Value), Rgb(light[$"--bdt-bs-link-{name}-hover-rgb"]));

            if (name is "light" or "dark") continue; // Bootstrap uses grays for their subtle colors
            var rootLight = Block(compiled, ":root,\n[data-bs-theme=light]");
            var rootDark = Block(compiled, "[data-bs-theme=dark]");
            foreach (var suffix in new[] { "text-emphasis", "bg-subtle", "border-subtle" })
            {
                AssertSameColor(rootLight[$"--bs-{name}-{suffix}"], light[$"--bs-{name}-{suffix}"], $"light --bs-{name}-{suffix}");
                AssertSameColor(rootDark[$"--bs-{name}-{suffix}"], dark[$"--bs-{name}-{suffix}"], $"dark --bs-{name}-{suffix}");
                checkedCount += 2;
            }
        }
        Assert.True(checkedCount > 90);
        Directory.Delete(work, true);
    }

    [Fact]
    public void Reference_mode_derives_shades_from_the_runtime_variable()
    {
        var bag = new DiagnosticBag();
        var d = BootstrapRuntime.GetDeclarations(Tokens(), Options(IntegrationValueMode.Reference), dark: false, bag).ToDictionary(x => x.Name, x => x.Value);
        Assert.Equal("var(--ds-c-primary)", d["--bs-primary"]);
        Assert.Equal("45, 109, 202", d["--bs-primary-rgb"]);
        Assert.Equal("color-mix(in srgb, var(--bs-primary) 85%, #000000)", d["--bdt-bs-btn-primary-hover-bg"]);   // shade: white text
        Assert.Equal("color-mix(in srgb, var(--bs-warning) 85%, #ffffff)", d["--bdt-bs-btn-warning-hover-bg"]);   // tint: black text
        Assert.Equal("color-mix(in srgb, var(--bs-warning) 90%, #ffffff)", d["--bdt-bs-btn-warning-hover-border-color"]);
        Assert.Equal("#ffffff", d["--bdt-bs-primary-contrast"]);
        Assert.Equal("#000000", d["--bdt-bs-warning-contrast"]);
    }

    [Fact]
    public void Themed_stylesheet_covers_every_theme_and_mode_and_overrides_bootstrap_root_rules()
    {
        var mappings = new List<TargetMapping> { new("primary", "color.surface.accent"), new("body-bg", "color.surface.default"), new("body-color", "color.text.default") };
        var output = BootstrapRuntime.ExportThemes(Catalog(), new BootstrapExportOptions { Mappings = mappings, Css = Fixture.Css });
        Assert.DoesNotContain(output.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var css = output.Content;
        Assert.Contains(":is(:root:not([data-theme]), [data-theme=\"aurora\"]) {", css);  // (0,2,0) beats Bootstrap's :root (0,1,0)
        Assert.Contains("[data-theme=\"ember\"]", css);
        Assert.Contains("[data-mode=\"dark\"]", css);
        Assert.Contains("@media (prefers-color-scheme: dark)", css);
        Assert.Contains("--bs-primary: var(--ds-color-surface-accent);", css);
        Assert.Contains("--bs-primary-rgb:", css);
        Assert.Contains(".btn-primary {", css);
        Assert.Contains(".nav-pills { --bs-nav-pills-link-active-bg: var(--bs-primary); }", css);
        Assert.DoesNotMatch(@"(?m)^\s+color-scheme:", css);
        // The -rgb companion differs between light and dark mode: each mode gets literal values.
        Assert.True(Regex.Matches(css, @"--bs-primary-rgb: \d+, \d+, \d+;").Select(m => m.Value).Distinct().Count() > 1);
    }

    [Fact]
    public void Themed_stylesheet_bridges_only_colors_with_derived_variables()
    {
        var mappings = new List<TargetMapping> { new("primary", "color.surface.accent"), new("danger", "color.overlay.scrim") };
        var css = BootstrapRuntime.ExportThemes(Catalog(), new BootstrapExportOptions { Mappings = mappings, Css = Fixture.Css }).Content;
        Assert.Contains(".btn-primary {", css);
        Assert.DoesNotContain(".btn-danger", css); // translucent: no derived variables in any theme, so no bridge rules
    }

    [Fact]
    public void Generated_runtime_css_is_valid_css()
    {
        var tools = Fixture.RequireTools();
        var work = Fixture.WorkDirectory(tools, "bootstrap-css");
        var mappings = new List<TargetMapping> { new("primary", "color.surface.accent"), new("danger", "color.surface.danger"), new("body-bg", "color.surface.default") };
        File.WriteAllText(Path.Combine(work, "themes.css"), BootstrapRuntime.ExportThemes(Catalog(), new BootstrapExportOptions { Mappings = mappings, Css = Fixture.Css }).Content);
        var (exit, log) = Fixture.RunNode(tools, work, Path.Combine(tools, "node_modules", "sass", "sass.js"), "--no-source-map", "themes.css", "out.css");
        Assert.True(exit == 0, log);
        Directory.Delete(work, true);
    }

    [Fact]
    public void Sass_output_gamut_maps_wide_gamut_colors()
    {
        var set = TokenResolver.Resolve("""{ "p": { "$type": "color", "$value": { "colorSpace": "display-p3", "components": [0, 0.8, 0.3] } } }""");
        var output = BootstrapExporter.Export(set, new BootstrapExportOptions { Mappings = [new("primary", "p")] });
        Assert.Contains(output.Diagnostics, d => d.RuleId == DiagnosticCatalog.OutOfGamut.Id);
        Assert.Matches(@"\$primary: #[0-9a-f]{6};", output.Content);
    }

    private static BlazorDesignToolkit.Theming.ThemeCatalog Catalog()
        => BlazorDesignToolkit.Theming.ThemeManifest.LoadAsync(Path.Combine(AppContext.BaseDirectory, "SampleTokens", "aurora.themes.json")).GetAwaiter().GetResult().Catalog!;

    private static Dictionary<string, string> Block(string css, string selector)
    {
        var index = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(index >= 0, $"Rule '{selector}' not found in compiled Bootstrap.");
        var body = css[(css.IndexOf('{', index) + 1)..css.IndexOf('}', index)];
        return body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static p => p.Contains(':'))
            .ToDictionary(p => p[..p.IndexOf(':')].Trim(), p => p[(p.IndexOf(':') + 1)..].Trim());
    }

    private static void AssertSameColor(string expected, string actual, string what)
    {
        var e = ParseColor(expected);
        var a = ParseColor(actual);
        // Hex output is quantized to 8 bits, Sass keeps fractions: allow half a step (plus floating-point noise).
        const double tolerance = 0.5 / 255 + 1e-6;
        Assert.True(Math.Abs(e.R - a.R) <= tolerance && Math.Abs(e.G - a.G) <= tolerance && Math.Abs(e.B - a.B) <= tolerance,
            $"{what}: Bootstrap compiled {expected}, toolkit computed {actual}.");
    }

    private static SrgbColor ParseColor(string text)
    {
        text = text.Trim();
        if (text == "#fff") text = "#ffffff";
        if (text == "#000") text = "#000000";
        if (SrgbColor.TryParseHex(text, out var hex)) return hex;
        if (text is "white") return new SrgbColor(1, 1, 1);
        if (text is "black") return new SrgbColor(0, 0, 0);
        var m = RgbFunction().Match(text);
        Assert.True(m.Success, $"Unrecognized color '{text}'.");
        return new SrgbColor(D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value));
        // Dart Sass prints fractional channels as percentages.
        static double D(string s) => s.EndsWith('%') ? double.Parse(s[..^1], CultureInfo.InvariantCulture) / 100 : double.Parse(s, CultureInfo.InvariantCulture) / 255;
    }

    private static (int, int, int) Rgb(string triplet)
    {
        var parts = triplet.Split(',').Select(static p => (int)Math.Round(double.Parse(p.Trim(), CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero)).ToArray();
        return (parts[0], parts[1], parts[2]);
    }

    [GeneratedRegex(@"rgba?\(\s*([\d.]+%?)[,\s]+([\d.]+%?)[,\s]+([\d.]+%?)")]
    private static partial Regex RgbFunction();

    [GeneratedRegex(@"color:RGBA\(([\d.]+,[\d.]+,[\d.]+),")]
    private static partial Regex LinkHoverRgb();
}
