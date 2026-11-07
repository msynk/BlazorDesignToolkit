using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Tests;

public class CssExportTests
{
    [Fact]
    public void Exports_all_types_to_expected_css()
    {
        var set = TestHelpers.ResolveFixture("conformance", "all-types.tokens.json");
        var result = CssExporter.Export(set, new CssExportOptions { Prefix = "ds" });
        Golden.AssertMatches("all-types.css", result.Css);
        // The strokeStyle object has no CSS equivalent and is reported, not silently converted.
        Assert.Equal("stroke.object", result.Diagnostics.Single(DiagnosticCatalog.UnsupportedConversion.Id).TokenPath);
    }

    [Fact]
    public void Preserve_mode_emits_var_references()
    {
        var set = TestHelpers.ResolveFixture("conformance", "composite-aliases.tokens.json");
        var css = CssExporter.Export(set, new CssExportOptions { References = CssReferenceMode.Preserve, IncludeHeader = false }).Css;
        Assert.Contains("--component-card-box-shadow: var(--shadow-medium);", css);
        Assert.Contains("--shadow-medium: var(--space-small) var(--space-small) 1.5rem 0rem var(--color-shadow-050);", css);
    }

    [Fact]
    public void Preserve_mode_falls_back_to_literal_when_target_is_filtered_out()
    {
        var set = TestHelpers.ResolveFixture("conformance", "composite-aliases.tokens.json");
        var css = CssExporter.Export(set, new CssExportOptions
        {
            References = CssReferenceMode.Preserve,
            Filter = t => t.Path.Segments[0] == "component",
        }).Css;
        Assert.Contains("--component-card-box-shadow: 0.5rem 0.5rem 1.5rem 0rem rgb(0 0 0 / 0.5);", css);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var a = CssExporter.Export(TestHelpers.ResolveSamples("foundation.tokens.json", "semantic.light.tokens.json", "density.comfortable.tokens.json", "components.tokens.json")).Css;
        var b = CssExporter.Export(TestHelpers.ResolveSamples("foundation.tokens.json", "semantic.light.tokens.json", "density.comfortable.tokens.json", "components.tokens.json")).Css;
        Assert.Equal(a, b);
        Assert.DoesNotContain("\r", a);
    }

    [Fact]
    public void Untrusted_strings_cannot_break_out_of_style_elements()
    {
        var set = TestHelpers.Resolve("""
        { "font": { "$type": "fontFamily", "evil": { "$value": "x\"; } </style><script>alert(1)</script>", "$description": "*/ </style> body{}" } } }
        """);
        var css = CssExporter.Export(set, new CssExportOptions { IncludeComments = true }).Css;
        Assert.DoesNotContain("</style>", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\\"", css);
        Assert.DoesNotContain("*/ ", css.Split('\n').Single(l => l.Contains("/* ") && !l.Contains("Generated")).Replace("/* ", "").Replace(" */", ""));
    }

    [Theory]
    [InlineData("fontSize", "font-size")]
    [InlineData("Hot pink", "hot-pink")]
    [InlineData("HTMLParser", "html-parser")]
    [InlineData("on-surface", "on-surface")]
    [InlineData("2xl", "2xl")]
    [InlineData("a/b:c", "a-b-c")]
    public void Kebab_case_names(string segment, string expected) => Assert.Equal(expected, CssExporter.ToKebab(segment));

    [Fact]
    public void Root_segment_is_omitted_and_collisions_are_reported()
    {
        var set = TestHelpers.Resolve("""
        { "accent": { "$root": { "$type": "number", "$value": 1 }, "fg": { "$type": "number", "$value": 2 } },
          "a": { "b": { "$type": "number", "$value": 3 } }, "a-b": { "$type": "number", "$value": 4 } }
        """);
        var result = CssExporter.Export(set);
        Assert.Contains("--accent: 1;", result.Css);
        Assert.Equal("a-b", result.Diagnostics.Single(DiagnosticCatalog.NameCollision.Id).TokenPath);
    }

    [Fact]
    public void Invalid_prefix_is_reported()
    {
        var result = CssExporter.Export(TestHelpers.Resolve("""{ "a": { "$type": "number", "$value": 1 } }"""), new CssExportOptions { Prefix = "x;}" });
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidIdentifier.Id);
    }

    [Fact]
    public void Color_formats()
    {
        var set = TestHelpers.Resolve("""
        { "c": { "$type": "color",
            "hex": { "$value": { "colorSpace": "srgb", "components": [1, 0.4, 0] } },
            "frac": { "$value": { "colorSpace": "srgb", "components": [0.5, 0.5, 0.5], "alpha": 0.25 } },
            "p3": { "$value": { "colorSpace": "display-p3", "components": [0, 1, 0] } },
            "ok": { "$value": { "colorSpace": "oklch", "components": [0.7, 0.1, "none"] } },
            "hsl": { "$value": { "colorSpace": "hsl", "components": [200, 50, 40] } } } }
        """);
        var auto = CssExporter.Export(set, new CssExportOptions { IncludeHeader = false }).Css;
        Assert.Contains("--c-hex: #ff6600;", auto);
        Assert.Contains("--c-frac: color(srgb 0.5 0.5 0.5 / 0.25);", auto);
        Assert.Contains("--c-p3: color(display-p3 0 1 0);", auto);
        Assert.Contains("--c-ok: oklch(0.7 0.1 none);", auto);
        Assert.Contains("--c-hsl: hsl(200 50% 40%);", auto);

        var hex = CssExporter.Export(set, new CssExportOptions { ColorFormat = CssColorFormat.Hex });
        Assert.Contains("--c-frac: #80808040;", hex.Css);
        Assert.Equal("c.p3", hex.Diagnostics.Single(DiagnosticCatalog.OutOfGamut.Id).TokenPath);
    }

    [Fact]
    public void Layer_and_selector_options()
    {
        var set = TestHelpers.Resolve("""{ "a": { "$type": "number", "$value": 1 } }""");
        var css = CssExporter.Export(set, new CssExportOptions { Layer = "tokens", Selector = ".theme", IncludeHeader = false }).Css;
        Assert.Equal("@layer tokens {\n  .theme {\n    --a: 1;\n  }\n}\n", css);
    }

    [Fact]
    public void Dtcg_json_exporter_writes_resolved_self_contained_tokens()
    {
        var set = TestHelpers.ResolveFixture("conformance", "composite-aliases.tokens.json");
        var output = new DtcgJsonTokenExporter().Export(new TokenExportContext(set));
        var reread = TestHelpers.Resolve(output.Content);
        reread.AssertNoErrors();
        Assert.All(reread.Tokens, t => Assert.False(t.IsAlias));
        Assert.Equal(set["component.card.box-shadow"]!.Value, reread["component.card.box-shadow"]!.Value);
        Assert.Equal(output.Content, new DtcgJsonTokenExporter().Export(new TokenExportContext(set)).Content);
    }

    [Fact]
    public void Exporter_registry_validates_settings()
    {
        var registry = TokenExporterRegistry.CreateDefault();
        var css = registry.Find("CSS")!;
        Assert.Empty(TokenExporterRegistry.ValidateSettings(css, new Dictionary<string, string> { ["prefix"] = "x" }));
        Assert.Single(TokenExporterRegistry.ValidateSettings(css, new Dictionary<string, string> { ["prefx"] = "x" }));
        var output = css.Export(new TokenExportContext(TestHelpers.Resolve("""{ "a": { "$type": "number", "$value": 1 } }"""),
            new Dictionary<string, string> { ["prefix"] = "bdt", ["references"] = "preserve", ["colorFormat"] = "hex" }));
        Assert.Contains("--bdt-a: 1;", output.Content);
    }

    [Fact]
    public void Writer_output_parses_back_to_same_tokens()
    {
        var set = TestHelpers.ResolveFixture("conformance", "all-types.tokens.json");
        var json = DtcgWriter.Write(set, new DtcgWriteOptions { PreserveReferences = true });
        var reread = TestHelpers.Resolve(json);
        foreach (var token in set.Tokens.Where(t => t.IsValid))
        {
            Assert.True(SourceNode.DeepEquals(token.Value!.ToSourceNode(), reread[token.Path]!.Value!.ToSourceNode()), token.Path.ToString());
        }
    }
}

/// <summary>Golden-file assertions. Set BDT_UPDATE_GOLDEN=1 to rewrite expected files in the source tree.</summary>
internal static class Golden
{
    public static void AssertMatches(string name, string actual)
    {
        var expectedPath = TestHelpers.FixturePath("golden", name);
        if (Environment.GetEnvironmentVariable("BDT_UPDATE_GOLDEN") == "1")
        {
            var source = Path.Combine(FindRepoRoot(), "tests", "BlazorDesignToolkit.Tokens.Tests", "Fixtures", "golden", name);
            File.WriteAllText(source, actual);
            return;
        }
        Assert.True(File.Exists(expectedPath), $"Golden file {name} is missing. Run tests with BDT_UPDATE_GOLDEN=1 to create it, then review it.");
        Assert.Equal(File.ReadAllText(expectedPath).ReplaceLineEndings("\n"), actual);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
