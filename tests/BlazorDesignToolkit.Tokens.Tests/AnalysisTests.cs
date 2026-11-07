using BlazorDesignToolkit.Tokens.Analysis;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;

namespace BlazorDesignToolkit.Tokens.Tests;

public class AnalysisTests
{
    private const string Tokens = """
    {
      "color": { "$type": "color",
        "blue": { "$value": { "colorSpace": "srgb", "components": [0, 0, 1] } },
        "red": { "$value": { "colorSpace": "srgb", "components": [1, 0, 0] } },
        "accent": { "$value": "{color.blue}" },
        "unused": { "$value": { "colorSpace": "srgb", "components": [0, 1, 0] } } },
      "type": { "body": { "$type": "typography", "$value": { "fontFamily": "Inter", "fontSize": { "value": 1, "unit": "rem" }, "fontWeight": 400, "letterSpacing": { "value": 0, "unit": "px" }, "lineHeight": 1.5 } } }
    }
    """;

    private static readonly SourceText[] s_sources =
    [
        new("src/Components/Button.razor.css", ".btn {\n  color: var(--ds-color-accent);\n  font-size: var(--ds-type-body-font-size);\n}\n"),
        new("src/Components/Card.razor", "<div style=\"background: var(--ds-color-acent)\"></div>\n<div style=\"color: var(--ds-color-@Tone)\"></div>"),
        new("src/site.css", ":root { --ds-local: 1px; }\n.x { margin: var(--ds-local); color: var(--other-lib); }"),
    ];

    [Fact]
    public void Coverage_maps_usages_unused_and_undefined_properties()
    {
        var set = TestHelpers.Resolve(Tokens);
        var report = TokenCoverageAnalyzer.Analyze(set, s_sources, new CoverageOptions { Css = new CssExportOptions { Prefix = "ds" } });

        Assert.Equal(3, report.ScannedFileCount);
        Assert.Contains(report.Usages, u => u.Token == TokenPath.Parse("color.accent") && u.Component == "Button" && u.Line == 2);
        Assert.Contains(report.Usages, u => u.Token == TokenPath.Parse("type.body") && u.Variable == "--ds-type-body-font-size");
        // color.blue is used transitively through color.accent.
        Assert.DoesNotContain("color.blue", report.UnusedTokens);
        Assert.Equal(["color.red", "color.unused"], report.UnusedTokens);
        Assert.Equal(["--ds-color-acent"], report.UndefinedVariables);
        Assert.Contains("--ds-color-accent", report.Diagnostics.Single(DiagnosticCatalog.UndefinedCustomProperty.Id).Remediation);
        Assert.Contains(report.Diagnostics, d => d.RuleId == DiagnosticCatalog.AnalysisLimit.Id && d.Location!.Line == 2);
        Assert.NotEmpty(report.Limitations);
        Assert.Equal(3, report.UsedTokenCount);
        Assert.Equal(60, report.CoveragePercent);
    }

    [Fact]
    public void Component_contracts()
    {
        var set = TestHelpers.Resolve(Tokens);
        var manifests = new[]
        {
            new ComponentManifest { Name = "Button", Sources = ["**/Button.razor*"], Tokens = ["color.red"], RequiredTokens = ["color.accent", "color.focus"] },
        };
        var report = TokenCoverageAnalyzer.Analyze(set, s_sources, new CoverageOptions { Css = new CssExportOptions { Prefix = "ds" }, Components = manifests });
        Assert.Equal("color.focus", report.Diagnostics.Single(DiagnosticCatalog.MissingContractToken.Id).TokenPath);
        Assert.Equal("type.body", report.Diagnostics.Single(DiagnosticCatalog.UndeclaredTokenUsage.Id).TokenPath);
        var button = report.Components.Single(c => c.Name == "Button");
        Assert.Equal(["color.accent", "type.body"], button.UsedTokens);
        Assert.Equal(["color.focus"], button.MissingRequired);
    }

    [Fact]
    public void Entry_points_count_as_used()
    {
        var report = TokenCoverageAnalyzer.Analyze(TestHelpers.Resolve(Tokens), [], new CoverageOptions { EntryPoints = ["color.**"] });
        Assert.Equal(["type.body"], report.UnusedTokens);
    }

    [Fact]
    public void Diff_classifies_changes()
    {
        var before = TestHelpers.Resolve(Tokens);
        var after = TestHelpers.Resolve(Tokens
            .Replace("\"accent\": { \"$value\": \"{color.blue}\" }", "\"accent\": { \"$value\": \"{color.red}\", \"$deprecated\": true }")
            .Replace("\"unused\": { \"$value\": { \"colorSpace\": \"srgb\", \"components\": [0, 1, 0] } }", "\"added\": { \"$value\": { \"colorSpace\": \"srgb\", \"components\": [0, 1, 0] } }"));
        var diff = TokenDiff.Compare(before, after);
        Assert.True(diff.HasBreakingChanges);
        Assert.Equal(1, diff.Count(TokenChangeKind.Added));
        Assert.Equal(1, diff.Count(TokenChangeKind.Removed));
        Assert.Contains(diff.Changes, c => c is { Path: "color.accent", Kind: TokenChangeKind.ValueChanged });
        Assert.Contains(diff.Changes, c => c is { Path: "color.accent", Kind: TokenChangeKind.ReferenceChanged, Before: "color.blue", After: "color.red" });
        Assert.Contains(diff.Changes, c => c is { Path: "color.accent", Kind: TokenChangeKind.Deprecated });
        Assert.False(TokenDiff.Compare(before, before).HasChanges);
    }

    [Fact]
    public void Github_annotations_escape_properties()
    {
        var d = new Diagnostic("BDT3001", DiagnosticSeverity.Error, "Missing, really\nmissing", "a.b", new SourceLocation("x:y.json", 3, 4));
        Assert.Equal("::error file=x%3Ay.json,line=3,col=4,title=BDT3001 a.b::Missing, really%0Amissing", DiagnosticJson.ToGitHubAnnotation(d));
    }
}
