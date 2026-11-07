using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Validation;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

public class ContrastTests
{
    private static ColorValue Hex(string hex) => ColorValue.FromHex(hex);

    [Theory]
    [InlineData("#000000", "#ffffff", 21.0)]
    [InlineData("#ffffff", "#ffffff", 1.0)]
    [InlineData("#767676", "#ffffff", 4.54)]
    [InlineData("#777777", "#ffffff", 4.48)]
    public void Wcag_contrast_ratio(string fg, string bg, double expected)
        => Assert.Equal(expected, Wcag.ContrastRatio(Hex(fg).ToSrgb(), Hex(bg).ToSrgb()), 2);

    [Fact]
    public void Ratio_is_never_rounded_up_across_the_threshold()
    {
        var result = ContrastEvaluator.Evaluate("fg", Hex("#777777"), "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA);
        Assert.False(result.Passed);
        Assert.Equal("4.47:1", result.RatioText);
        Assert.NotNull(result.SuggestedForeground);
        var suggested = ContrastEvaluator.Evaluate("fg", Hex(result.SuggestedForeground!), "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA);
        Assert.True(suggested.Passed);
    }

    [Fact]
    public void Translucent_foreground_is_composited_over_background()
    {
        var black50 = new ColorValue(ColorSpaces.Srgb, [0, 0, 0], 0.5);
        var result = ContrastEvaluator.Evaluate("fg", black50, "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA);
        Assert.Equal("#808080", result.EffectiveForegroundHex);
        Assert.Equal(3.98, result.Ratio!.Value, 2);
    }

    [Fact]
    public void Translucent_background_without_backdrop_is_indeterminate()
    {
        var scrim = new ColorValue(ColorSpaces.Srgb, [0, 0, 0], 0.5);
        var result = ContrastEvaluator.Evaluate("fg", Hex("#ffffff"), "bg", scrim, ContrastKind.Text, ConformanceLevel.AA);
        Assert.Null(result.Passed);
        Assert.Contains("translucent", result.IndeterminateReason);

        var withBackdrop = ContrastEvaluator.Evaluate("fg", Hex("#ffffff"), "bg", scrim, ContrastKind.Text, ConformanceLevel.AA, Hex("#ffffff"));
        Assert.NotNull(withBackdrop.Passed);
    }

    [Fact]
    public void Out_of_gamut_and_none_components_are_indeterminate()
    {
        var p3 = new ColorValue(ColorSpaces.DisplayP3, [0, 1, 0]);
        Assert.Null(ContrastEvaluator.Evaluate("fg", p3, "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA).Passed);
        var none = new ColorValue(ColorSpaces.Hsl, [null, 0, 50]);
        Assert.Null(ContrastEvaluator.Evaluate("fg", none, "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA).Passed);
    }

    [Fact]
    public void In_gamut_wide_gamut_colors_are_evaluated()
    {
        var p3Gray = new ColorValue(ColorSpaces.DisplayP3, [0.2, 0.2, 0.2]);
        Assert.True(ContrastEvaluator.Evaluate("fg", p3Gray, "bg", Hex("#ffffff"), ContrastKind.Text, ConformanceLevel.AA).Passed);
    }
}

public class ValidationTests
{
    private const string Palette = """
    {
      "color": {
        "$type": "color",
        "white": { "$value": { "colorSpace": "srgb", "components": [1, 1, 1], "hex": "#ffffff" } },
        "gray": { "$value": { "colorSpace": "srgb", "components": [0.6, 0.6, 0.6], "hex": "#999999" } },
        "ink": { "$value": { "colorSpace": "srgb", "components": [0.1, 0.1, 0.1], "hex": "#1a1a1a" } },
        "surface": { "default": { "$value": "{color.white}" }, "danger": { "$value": "{color.ink}" } },
        "on-surface": { "default": { "$value": "{color.gray}" } },
        "focus": { "$value": "{color.gray}" }
      }
    }
    """;

    [Fact]
    public void Contrast_pairs_fail_with_ratio_and_remediation()
    {
        var set = TestHelpers.Resolve(Palette);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            Accessibility = new AccessibilityRules { Pairs = [new ContrastPair("color.on-surface.default", "color.surface.default")] },
        });
        var d = report.Diagnostics.Single(DiagnosticCatalog.TextContrast.Id);
        Assert.Equal(">= 4.5:1", d.Expected);
        Assert.Equal("2.84:1", d.Actual);
        Assert.Contains("would pass", d.Remediation);
        Assert.True(report.Failed);
        Assert.Single(report.ContrastResults);
    }

    [Fact]
    public void Pairing_conventions_detect_missing_foregrounds()
    {
        var set = TestHelpers.Resolve(Palette);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            Accessibility = new AccessibilityRules { Pairings = [new ContrastPairing("color.surface.*", "color.on-surface.*", ContrastKind.LargeText)] },
        });
        var missing = report.Diagnostics.Single(DiagnosticCatalog.MissingColorPair.Id);
        Assert.Equal("color.surface.danger", missing.TokenPath);
        Assert.Equal("color.on-surface.danger", missing.Expected);
        Assert.Equal(ContrastKind.LargeText, Assert.Single(report.ContrastResults).Kind);
    }

    [Fact]
    public void Focus_indicator_rule_checks_every_background()
    {
        var set = TestHelpers.Resolve(Palette);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            Accessibility = new AccessibilityRules { FocusIndicators = [new FocusIndicatorRule("color.focus", ["color.surface.*"])] },
        });
        Assert.Equal(2, report.ContrastResults.Count);
        Assert.Equal("color.focus", report.Diagnostics.Single(DiagnosticCatalog.FocusIndicatorContrast.Id).TokenPath);
    }

    [Fact]
    public void Aaa_is_optional()
    {
        var set = TestHelpers.Resolve(Palette);
        var options = new ValidationOptions { Accessibility = new AccessibilityRules { Pairs = [new ContrastPair("color.ink", "color.gray")] } };
        Assert.DoesNotContain(TokenValidator.Validate(set, options).Diagnostics, d => d.RuleId == DiagnosticCatalog.EnhancedTextContrast.Id);
        var aaa = TokenValidator.Validate(set, options with { Accessibility = options.Accessibility with { IncludeAaa = true } });
        Assert.Contains(aaa.Diagnostics, d => d.RuleId == DiagnosticCatalog.EnhancedTextContrast.Id);
    }

    [Fact]
    public void Invalid_contrast_pair_configuration_is_reported()
    {
        var set = TestHelpers.Resolve(Palette);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            Accessibility = new AccessibilityRules { Pairs = [new ContrastPair("color.nope", "color.white"), new ContrastPair("color.white", "color")] },
        });
        Assert.Equal(2, report.Diagnostics.Count(d => d.RuleId == DiagnosticCatalog.InvalidContrastPair.Id));
    }

    [Fact]
    public void Naming_rules_suggest_a_fix()
    {
        var set = TestHelpers.Resolve("""{ "colorBrand": { "Primary_Light": { "$type": "number", "$value": 1 } } }""");
        var report = TokenValidator.Validate(set, new ValidationOptions { Naming = [new NamingRule { Case = NameCase.KebabCase }] });
        var d = report.Diagnostics.Single(DiagnosticCatalog.NamingConvention.Id);
        Assert.Contains("'color-brand'", d.Remediation);
    }

    [Theory]
    [InlineData("on-surface", NameCase.KebabCase, true)]
    [InlineData("100", NameCase.KebabCase, true)]
    [InlineData("onSurface", NameCase.CamelCase, true)]
    [InlineData("OnSurface", NameCase.PascalCase, true)]
    [InlineData("on_surface", NameCase.SnakeCase, true)]
    [InlineData("On-Surface", NameCase.KebabCase, false)]
    public void Name_cases(string segment, NameCase nameCase, bool ok)
        => Assert.Equal(ok, TokenValidator.Validate(TestHelpers.Resolve($$"""{ "{{segment}}": { "$type": "number", "$value": 1 } }"""),
            new ValidationOptions { Naming = [new NamingRule { Case = nameCase }] }).Diagnostics.All(d => d.RuleId != DiagnosticCatalog.NamingConvention.Id));

    [Fact]
    public void Required_tokens_and_types()
    {
        var set = TestHelpers.Resolve(Palette);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            RequiredTokens = [new RequiredToken("color.text.default"), new RequiredToken("color.white", "dimension"), new RequiredToken("color.ink", "color")],
        });
        Assert.Equal("color.text.default", report.Diagnostics.Single(DiagnosticCatalog.MissingRequiredToken.Id).TokenPath);
        Assert.Equal("color.white", report.Diagnostics.Single(DiagnosticCatalog.RequiredTokenWrongType.Id).TokenPath);
    }

    [Fact]
    public void Duplicate_raw_values_are_reported_as_info()
    {
        var set = TestHelpers.Resolve("""{ "n": { "$type": "number", "a": { "$value": 4 }, "b": { "$value": 4 }, "c": { "$value": "{n.a}" } }, "other": { "$type": "number", "$value": 4 } }""");
        var d = TokenValidator.Validate(set).Diagnostics.Single(DiagnosticCatalog.DuplicateValue.Id);
        Assert.Equal("n.b", d.TokenPath); // aliases and tokens in other top-level groups are not duplicates
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    [Fact]
    public void Inconsistent_scales_are_detected()
    {
        var set = TestHelpers.Resolve("""
        { "space": { "$type": "dimension",
            "1": { "$value": { "value": 4, "unit": "px" } }, "2": { "$value": { "value": 8, "unit": "px" } },
            "3": { "$value": { "value": 6, "unit": "px" } }, "4": { "$value": { "value": 1, "unit": "rem" } } },
          "size": { "$type": "dimension", "sm": { "$value": { "value": 1, "unit": "px" } }, "md": { "$value": { "value": 2, "unit": "px" } }, "lg": { "$value": { "value": 3, "unit": "px" } } } }
        """);
        var d = TokenValidator.Validate(set).Diagnostics.Single(DiagnosticCatalog.InconsistentScale.Id);
        Assert.Equal("space.3", d.TokenPath);
    }

    [Fact]
    public void Sample_palette_ramps_are_monotonic()
    {
        var set = TestHelpers.ResolveSamples("foundation.tokens.json");
        Assert.DoesNotContain(TokenValidator.Validate(set).Diagnostics, d => d.RuleId == DiagnosticCatalog.InconsistentScale.Id);
    }

    [Fact]
    public void Deprecated_references_are_reported()
    {
        var set = TestHelpers.Resolve("""{ "old": { "$type": "number", "$value": 1, "$deprecated": "Use new" }, "user": { "$value": "{old}" } }""");
        var report = TokenValidator.Validate(set);
        Assert.Contains("Use new", report.Diagnostics.Single(DiagnosticCatalog.DeprecatedReference.Id).Message);
        Assert.Equal("old", report.Diagnostics.Single(DiagnosticCatalog.DeprecatedToken.Id).TokenPath);
    }

    [Fact]
    public void Layer_rules()
    {
        var set = TestHelpers.Resolve("""
        {
          "palette": { "$type": "number", "one": { "$value": 1 }, "bad": { "$value": "{semantic.ok}" } },
          "semantic": { "$type": "number", "ok": { "$value": "{palette.one}" }, "raw": { "$value": 2 } },
          "component": { "$type": "number", "direct": { "$value": "{palette.one}" }, "good": { "$value": "{semantic.ok}" } }
        }
        """);
        var report = TokenValidator.Validate(set, new ValidationOptions
        {
            Layers = new TokenLayerConvention { Primitive = ["palette.**"], Semantic = ["semantic.**"], Component = ["component.**"] },
        });
        var violations = report.Diagnostics.Where(d => d.RuleId == DiagnosticCatalog.LayerViolation.Id).Select(d => d.TokenPath).Order().ToList();
        Assert.Equal(["component.direct", "palette.bad", "semantic.raw"], violations);
    }

    [Fact]
    public void Policy_overrides_suppressions_and_thresholds()
    {
        var set = TestHelpers.Resolve("""{ "n": { "$type": "number", "a": { "$value": 4 }, "b": { "$value": 4 } }, "x": { "$value": "{missing}" } }""");
        var policy = new DiagnosticPolicy
        {
            SeverityOverrides = { [DiagnosticCatalog.DuplicateValue.Id] = DiagnosticSeverity.Warning },
            Suppressions = [new DiagnosticSuppression(DiagnosticCatalog.MissingReference.Id, "Provided by another package at build time", "x")],
            FailOn = DiagnosticSeverity.Warning,
        };
        var report = TokenValidator.Validate(set, new ValidationOptions { Policy = policy });
        Assert.Equal(DiagnosticSeverity.Warning, report.Diagnostics.Single(DiagnosticCatalog.DuplicateValue.Id).Severity);
        var suppressed = report.Result.Suppressed.Single(DiagnosticCatalog.MissingReference.Id);
        Assert.Equal("Provided by another package at build time", suppressed.SuppressionJustification);
        Assert.True(report.Failed);

        var disabled = TokenValidator.Validate(set, new ValidationOptions
        {
            Policy = new DiagnosticPolicy { SeverityOverrides = { [DiagnosticCatalog.DuplicateValue.Id] = DiagnosticSeverity.None } },
        });
        Assert.DoesNotContain(disabled.Diagnostics, d => d.RuleId == DiagnosticCatalog.DuplicateValue.Id);
    }

    [Fact]
    public void Suppressions_require_justification()
    {
        var policy = new DiagnosticPolicy { Suppressions = [new DiagnosticSuppression(DiagnosticCatalog.DuplicateValue.Id, " ")] };
        var report = TokenValidator.Validate(TestHelpers.Resolve("""{ "n": { "$type": "number", "a": { "$value": 4 }, "b": { "$value": 4 } } }"""),
            new ValidationOptions { Policy = policy });
        Assert.Contains(report.Diagnostics, d => d.RuleId == DiagnosticCatalog.SuppressionWithoutJustification.Id);
        Assert.Contains(report.Diagnostics, d => d.RuleId == DiagnosticCatalog.DuplicateValue.Id); // not suppressed
    }

    [Fact]
    public void Scope_is_attached_to_diagnostics()
    {
        var report = TokenValidator.Validate(TestHelpers.Resolve("""{ "x": { "$value": "{missing}" } }"""), scope: "theme=aurora mode=dark");
        Assert.All(report.Diagnostics, d => Assert.Equal("theme=aurora mode=dark", d.Scope));
    }

    [Theory]
    [InlineData("color.*", "color.text", true)]
    [InlineData("color.*", "color.text.x", false)]
    [InlineData("color.**", "color.text.x", true)]
    [InlineData("**.hover", "a.b.hover", true)]
    [InlineData("color.on-*", "color.on-surface", true)]
    [InlineData("color.on-*", "color.surface", false)]
    public void Token_path_patterns(string pattern, string path, bool expected) => Assert.Equal(expected, TokenPathPattern.IsMatch(pattern, path));

    [Fact]
    public void Every_rule_has_a_unique_stable_identifier()
    {
        Assert.Equal(DiagnosticCatalog.All.Count, DiagnosticCatalog.All.Select(d => d.Id).Distinct().Count());
        Assert.All(DiagnosticCatalog.All, d => Assert.Matches("^BDT[1-9][0-9]{3}$", d.Id));
        Assert.All(TokenValidator.BuiltInRules.SelectMany(r => r.RuleIds), id => Assert.NotNull(DiagnosticCatalog.TryGet(id)));
    }
}
