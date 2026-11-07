using System.Collections.Immutable;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Theming.Tests;

internal static class Samples
{
    private static readonly Lazy<ThemeCatalog> s_catalog = new(() =>
    {
        var result = ThemeManifest.LoadAsync(Path.Combine(AppContext.BaseDirectory, "SampleTokens", "aurora.themes.json")).GetAwaiter().GetResult();
        Assert.True(result.Catalog is not null, string.Join("\n", result.Diagnostics));
        return result.Catalog!;
    });

    public static ThemeCatalog Catalog => s_catalog.Value;

    public static SourceObject Json(string json) => (SourceObject)SourceJsonParser.Parse(json).Root!;

    public static ThemeCatalog Small(Action<ThemeCatalogBuilder>? configure = null)
    {
        var builder = new ThemeCatalogBuilder().AddAxis(ThemeAxes.ColorMode()).AddAxis(ThemeAxes.DensityProfile());
        builder.AddTheme(new ThemeDefinition("base",
        [
            new ThemeLayer(Json("""{ "c": { "$type": "number", "bg": { "$value": 1 }, "fg": { "$value": 2 } }, "s": { "$type": "number", "pad": { "$value": 8 } } }"""), "base"),
            new ThemeLayer(Json("""{ "c": { "bg": { "$value": 10 } } }"""), "dark", new Dictionary<string, string> { ["mode"] = "dark" }),
            new ThemeLayer(Json("""{ "s": { "pad": { "$value": 4 } } }"""), "compact", new Dictionary<string, string> { ["density"] = "compact" }),
        ]), isDefault: true);
        configure?.Invoke(builder);
        return builder.Build();
    }

    public static double Number(ThemeResolution r, string path) => ((NumberValue)r.Tokens[TokenPath.Parse(path)]!.Value!).Value;
}

public class ThemeCatalogTests
{
    [Fact]
    public void Sample_manifest_loads_with_axes_and_themes()
    {
        var catalog = Samples.Catalog;
        Assert.Equal(["mode", "contrast", "density"], catalog.Axes.Select(a => a.Name));
        Assert.Equal(["aurora", "ember"], catalog.Themes.Select(t => t.Id));
        Assert.Equal("aurora", catalog.DefaultThemeId);
        Assert.Equal("1.0.0", catalog.FindTheme("aurora")!.Metadata["version"]);
    }

    [Fact]
    public void Every_sample_combination_resolves_without_errors()
    {
        var catalog = Samples.Catalog;
        foreach (var theme in catalog.Themes)
        {
            foreach (var axes in catalog.EnumerateAxisCombinations())
            {
                var resolution = catalog.Resolve(new ThemeSelection(theme.Id, axes));
                Assert.False(resolution.HasErrors, $"{resolution.Selection}: " + string.Join("\n", resolution.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                Assert.DoesNotContain(resolution.Diagnostics, d => d.RuleId == DiagnosticCatalog.OverrideAddsToken.Id);
            }
        }
    }

    [Fact]
    public void Layers_apply_by_condition_and_inheritance()
    {
        var catalog = Samples.Catalog;
        var auroraLight = catalog.Resolve(new ThemeSelection("aurora"));
        var auroraDark = catalog.Resolve(new ThemeSelection("aurora", new Dictionary<string, string> { ["mode"] = "dark" }));
        var emberLight = catalog.Resolve(new ThemeSelection("ember"));
        Assert.Equal(TokenPath.Parse("color.white"), auroraLight.Tokens["color.surface.default"]!.AliasOf);
        Assert.Equal(TokenPath.Parse("color.neutral.950"), auroraDark.Tokens["color.surface.default"]!.AliasOf);
        Assert.Equal(TokenPath.Parse("color.ember.700"), emberLight.Tokens["color.surface.accent"]!.AliasOf);
        // Component tokens alias semantic tokens, so the brand change flows through without redefining components.
        Assert.Equal(emberLight.Tokens["color.ember.700"]!.Value, emberLight.Tokens["component.button.primary.background"]!.Value);
    }

    [Fact]
    public void Normalize_falls_back_predictably()
    {
        var catalog = Samples.Small();
        var normalized = catalog.Normalize(new ThemeSelection("missing", new Dictionary<string, string> { ["mode"] = "sepia", ["other"] = "x" }), out var diagnostics);
        Assert.Equal("base", normalized.ThemeId);
        Assert.Equal("light", normalized["mode"]);
        Assert.Equal("comfortable", normalized["density"]);
        Assert.Contains(diagnostics, d => d.RuleId == DiagnosticCatalog.ThemeNotFound.Id);
        Assert.Equal(2, diagnostics.Count(d => d.RuleId == DiagnosticCatalog.InvalidThemeSelection.Id));
    }

    [Fact]
    public void System_values_resolve_from_preferences_or_default()
    {
        var catalog = Samples.Small();
        var selection = new ThemeSelection("base", new Dictionary<string, string> { ["mode"] = "system" });
        Assert.Equal("dark", catalog.ResolveSystemValues(selection, new SystemPreferences { ColorScheme = "dark" })["mode"]);
        Assert.Equal("light", catalog.ResolveSystemValues(selection, SystemPreferences.Unknown)["mode"]);
        Assert.Equal(10, Samples.Number(catalog.Resolve(selection.WithAxis("mode", "dark")), "c.bg"));
    }

    [Fact]
    public void Builder_reports_inheritance_problems()
    {
        var layer = new ThemeLayer(Samples.Json("""{ "a": { "$type": "number", "$value": 1 } }"""));
        var catalog = new ThemeCatalogBuilder()
            .AddTheme(new ThemeDefinition("a", [layer]) { Extends = "b" })
            .AddTheme(new ThemeDefinition("b", [layer]) { Extends = "a" })
            .AddTheme(new ThemeDefinition("c", [layer]) { Extends = "nope" })
            .AddTheme(new ThemeDefinition("a", [layer]))
            .AddTheme(new ThemeDefinition("d", [new ThemeLayer(layer.Tokens, "x", new Dictionary<string, string> { ["mode"] = "dark" })]))
            .Build();
        Assert.Contains(catalog.Diagnostics, d => d.RuleId == DiagnosticCatalog.CircularThemeInheritance.Id);
        Assert.Contains(catalog.Diagnostics, d => d.RuleId == DiagnosticCatalog.ThemeNotFound.Id && d.Actual == "nope");
        Assert.Contains(catalog.Diagnostics, d => d.RuleId == DiagnosticCatalog.DuplicateTheme.Id);
        Assert.Contains(catalog.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidThemeSelection.Id);
    }

    [Fact]
    public void Invalid_identifiers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ThemeDefinition("bad id\"]", []));
        Assert.Throws<ArgumentException>(() => new ThemeAxisDefinition("mode", ["light", "system"], "light"));
        Assert.Throws<ArgumentException>(() => new ThemeAxisDefinition("mode", ["light", "dark"], "light")
        {
            SystemMediaQueries = ImmutableDictionary<string, string>.Empty.Add("light", "(prefers-color-scheme: light)"),
        });
        Assert.Throws<ArgumentException>(() => new ThemeAxisDefinition("mode", ["light", "dark"], "light")
        {
            SystemMediaQueries = ImmutableDictionary<string, string>.Empty.Add("dark", "screen; } body { color: red"),
        });
    }

    [Fact]
    public void Variant_layers_that_add_or_retype_tokens_are_diagnosed()
    {
        var catalog = Samples.Small(b => b.AddTheme(new ThemeDefinition("odd",
        [
            new ThemeLayer(Samples.Json("""{ "a": { "$type": "number", "$value": 1 } }"""), "base"),
            new ThemeLayer(Samples.Json("""{ "a": { "$type": "dimension", "$value": { "value": 1, "unit": "px" } }, "typo": { "$type": "number", "$value": 2 } }"""),
                "dark", new Dictionary<string, string> { ["mode"] = "dark" }),
        ])));
        var resolution = catalog.Resolve(new ThemeSelection("odd", new Dictionary<string, string> { ["mode"] = "dark" }));
        Assert.Equal("typo", resolution.Diagnostics.Single(d => d.RuleId == DiagnosticCatalog.OverrideAddsToken.Id).TokenPath);
        Assert.Equal("a", resolution.Diagnostics.Single(d => d.RuleId == DiagnosticCatalog.OverrideTypeMismatch.Id).TokenPath);
        Assert.All(resolution.Diagnostics, d => Assert.Equal("theme=odd density=comfortable mode=dark", d.Scope));
    }

    [Fact]
    public void Runtime_overrides_apply_last_and_are_not_cached()
    {
        var catalog = Samples.Small();
        var selection = catalog.DefaultSelection;
        var overridden = catalog.Resolve(selection, Samples.Json("""{ "c": { "fg": { "$value": 99 } } }"""));
        Assert.Equal(99, Samples.Number(overridden, "c.fg"));
        Assert.Equal(2, Samples.Number(catalog.Resolve(selection), "c.fg"));
    }

    [Fact]
    public async Task Resolver_based_theme_maps_modifiers_to_axes()
    {
        var root = Samples.Json("""{ "themes": [{ "id": "spec", "resolver": "aurora.resolver.json" }] }""");
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleTokens");
        var result = await ThemeManifest.LoadAsync(root, directory, new Tokens.Resolution.FileSystemResolverSourceLoader(directory), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Catalog is not null, string.Join("\n", result.Diagnostics));
        Assert.Equal(["mode", "density"], result.Catalog!.Axes.Select(a => a.Name));
        Assert.Equal(["light", "dark", "lightMoreContrast", "darkMoreContrast"], result.Catalog.FindAxis("mode")!.Values);
        var dark = result.Catalog.Resolve(new ThemeSelection("spec", new Dictionary<string, string> { ["mode"] = "darkMoreContrast", ["density"] = "compact" }));
        Assert.False(dark.HasErrors, string.Join("\n", dark.Diagnostics));
        Assert.Equal(TokenPath.Parse("color.neutral.100"), dark.Tokens["color.text.muted"]!.AliasOf);
        Assert.Empty(ThemeCompletenessAnalyzer.Analyze(result.Catalog).Diagnostics);
    }

    [Fact]
    public void Completeness_analyzer_reports_asymmetric_alternatives()
    {
        var builder = new ThemeCatalogBuilder().AddAxis(ThemeAxes.ColorMode());
        builder.AddTheme(new ThemeDefinition("t",
        [
            new ThemeLayer(Samples.Json("""{ "a": { "$type": "number", "$value": 1 }, "b": { "$type": "number", "$value": 1 } }"""), "light", new Dictionary<string, string> { ["mode"] = "light" }),
            new ThemeLayer(Samples.Json("""{ "a": { "$type": "number", "$value": 2 } }"""), "dark", new Dictionary<string, string> { ["mode"] = "dark" }),
        ]));
        var (diagnostics, _) = ThemeCompletenessAnalyzer.Analyze(builder.Build());
        var d = diagnostics.Single(x => x.RuleId == DiagnosticCatalog.IncompleteThemeContext.Id);
        Assert.Equal("b", d.TokenPath);
    }
}

public class ThemeStylesheetTests
{
    [Fact]
    public void Every_combination_resolves_to_the_composed_values_through_the_cascade()
    {
        var catalog = Samples.Catalog;
        var options = ThemeCssOptions.Default;
        var result = ThemeStylesheetGenerator.Generate(catalog, options);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var cascade = new CssCascade(result.Css);
        foreach (var theme in catalog.Themes)
        {
            foreach (var axes in catalog.EnumerateAxisCombinations())
            {
                var expected = CssExporter.GetDeclarations(catalog.Resolve(new ThemeSelection(theme.Id, axes)).Tokens, options.Css, new DiagnosticBag())
                    .ToDictionary(d => d.Name, d => d.Value);
                expected["color-scheme"] = axes["mode"];
                var attributes = axes.ToDictionary(a => "data-" + a.Key, a => a.Value);
                attributes["data-theme"] = theme.Id;
                Assert.Equal(expected.OrderBy(kv => kv.Key), cascade.Evaluate(attributes).OrderBy(kv => kv.Key));
            }
        }
    }

    [Fact]
    public void System_value_works_through_media_queries_without_javascript()
    {
        var catalog = Samples.Catalog;
        var css = ThemeStylesheetGenerator.Generate(catalog).Css;
        var cascade = new CssCascade(css);
        var attributes = new Dictionary<string, string> { ["data-theme"] = "ember", ["data-mode"] = "system", ["data-contrast"] = "system", ["data-density"] = "compact" };
        var darkMore = cascade.Evaluate(attributes, new HashSet<string> { "(prefers-color-scheme: dark)", "(prefers-contrast: more)" });
        var explicitDarkMore = cascade.Evaluate(new Dictionary<string, string> { ["data-theme"] = "ember", ["data-mode"] = "dark", ["data-contrast"] = "more", ["data-density"] = "compact" });
        Assert.Equal(explicitDarkMore.OrderBy(kv => kv.Key), darkMore.OrderBy(kv => kv.Key));
        var noPreference = cascade.Evaluate(attributes);
        var light = cascade.Evaluate(new Dictionary<string, string> { ["data-theme"] = "ember", ["data-density"] = "compact" });
        Assert.Equal(light.OrderBy(kv => kv.Key), noPreference.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void Default_theme_applies_to_root_without_attributes()
    {
        var cascade = new CssCascade(ThemeStylesheetGenerator.Generate(Samples.Catalog).Css);
        var withoutAttributes = cascade.Evaluate(new Dictionary<string, string>());
        var aurora = cascade.Evaluate(new Dictionary<string, string> { ["data-theme"] = "aurora" });
        Assert.Equal(aurora.OrderBy(kv => kv.Key), withoutAttributes.OrderBy(kv => kv.Key));
        Assert.Equal("light", withoutAttributes["color-scheme"]);
    }

    [Fact]
    public void Variant_rules_only_contain_changed_declarations()
    {
        var result = ThemeStylesheetGenerator.Generate(Samples.Small());
        var compact = result.Blocks.Single(b => b.Assignment.Count == 1 && b.Assignment.GetValueOrDefault("density") == "compact");
        Assert.Equal(["--s-pad"], compact.Declarations.Select(d => d.Name));
        // dark + compact changes nothing beyond the single-axis rules, so no combined rule is emitted.
        Assert.DoesNotContain(result.Blocks, b => b.Assignment.Count == 2);
    }

    [Fact]
    public void Output_is_deterministic_and_layer_wrapped()
    {
        var options = ThemeCssOptions.Default with { Layer = "tokens" };
        var a = ThemeStylesheetGenerator.Generate(Samples.Catalog, options).Css;
        var b = ThemeStylesheetGenerator.Generate(Samples.Catalog, options).Css;
        Assert.Equal(a, b);
        Assert.StartsWith(CssExporter.Banner + "\n@layer tokens {\n", a);
        Assert.EndsWith("}\n", a);
    }

    [Fact]
    public void Invalid_attribute_configuration_is_rejected()
    {
        var result = ThemeStylesheetGenerator.Generate(Samples.Small(), ThemeCssOptions.Default with { ThemeAttribute = "data-x\"]{}" });
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidIdentifier.Id);
        Assert.Equal("", result.Css);
    }

    [Fact]
    public void Override_declarations_include_dependents_in_resolve_mode_only_when_changed()
    {
        var catalog = Samples.Catalog;
        var overrides = Samples.Json("""{ "color": { "blue": { "600": { "$value": { "colorSpace": "srgb", "components": [1, 0, 0] } } } } }""");
        var preserve = ThemeStylesheetGenerator.GetOverrideDeclarations(catalog, catalog.DefaultSelection, overrides, null, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Equal(["--color-blue-600"], preserve.Select(d => d.Name));
        var resolveMode = ThemeCssOptions.Default with { Css = ThemeCssOptions.Default.Css with { References = CssReferenceMode.Resolve } };
        var resolved = ThemeStylesheetGenerator.GetOverrideDeclarations(catalog, catalog.DefaultSelection, overrides, resolveMode, out _);
        Assert.Contains(resolved, d => d.Name == "--color-surface-accent" && d.Value == "#ff0000");
        Assert.Contains(resolved, d => d.Name == "--component-button-primary-background");
    }
}
