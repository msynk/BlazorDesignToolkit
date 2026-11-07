using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

public class ReferenceResolutionTests
{
    private const string Chain = """
    {
      "a": { "$type": "number", "$value": "{b}" },
      "b": { "$value": "{c}" },
      "c": { "$type": "number", "$value": 42 }
    }
    """;

    [Fact]
    public void Resolves_forward_references_and_chains()
    {
        var set = TestHelpers.Resolve(Chain);
        set.AssertNoErrors();
        Assert.Equal(42, ((NumberValue)set["a"]!.Value!).Value);
        Assert.Equal(TokenTypes.Number, set["b"]!.Type); // type comes from the referenced token
        Assert.Equal([TokenPath.Parse("b"), TokenPath.Parse("c")], set.GetAliasChain("a"));
        Assert.Equal([TokenPath.Parse("a")], set.GetDependents("b"));
    }

    [Fact]
    public void Missing_reference_reports_chain_and_suggestion()
    {
        var set = TestHelpers.Resolve("""{ "color": { "base": { "$type": "number", "$value": 1 } }, "x": { "$value": "{color.bse}" } }""");
        var d = set.Diagnostics.Single(DiagnosticCatalog.MissingReference.Id);
        Assert.Equal("x", d.TokenPath);
        Assert.Equal(["x", "color.bse"], d.ReferenceChain);
        Assert.Contains("{color.base}", d.Remediation);
        Assert.NotNull(d.Location);
        Assert.False(set["x"]!.IsValid);
    }

    [Fact]
    public void Detects_cycles_once_with_full_chain()
    {
        var set = TestHelpers.Resolve("""
        {
          "a": { "$type": "number", "$value": "{b}" },
          "b": { "$type": "number", "$value": "{c}" },
          "c": { "$type": "number", "$value": "{a}" },
          "d": { "$type": "number", "$value": "{a}" }
        }
        """);
        var cycle = set.Diagnostics.Single(DiagnosticCatalog.CircularReference.Id);
        Assert.Equal(["a", "b", "c", "a"], cycle.ReferenceChain);
        Assert.Contains("a -> b -> c -> a", cycle.Message);
        // The dependent outside the cycle points at the root cause; cycle members are not double-reported.
        var dependent = set.Diagnostics.Single(DiagnosticCatalog.DependsOnInvalidToken.Id);
        Assert.Equal("d", dependent.TokenPath);
        Assert.All(new[] { "a", "b", "c", "d" }, p => Assert.False(set[p]!.IsValid));
    }

    [Fact]
    public void Self_reference_is_a_cycle()
    {
        var set = TestHelpers.Resolve("""{ "a": { "$type": "number", "$value": "{a}" } }""");
        Assert.Equal(["a", "a"], set.Diagnostics.Single(DiagnosticCatalog.CircularReference.Id).ReferenceChain);
    }

    [Fact]
    public void Cycles_through_composite_sub_values_are_detected()
    {
        var set = TestHelpers.Resolve("""
        {
          "w": { "$type": "dimension", "$value": "{b.$root}" },
          "b": { "$root": { "$type": "border", "$value": { "color": { "colorSpace": "srgb", "components": [0,0,0] }, "width": "{w}", "style": "solid" } } }
        }
        """);
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.CircularReference.Id);
    }

    [Fact]
    public void Results_do_not_depend_on_document_order()
    {
        var tokens = Enumerable.Range(0, 40).Select(i => $"\"t{i}\": {{ \"$value\": \"{{t{i + 1}}}\" }}").ToList();
        tokens.Add("\"t40\": { \"$type\": \"dimension\", \"$value\": { \"value\": 4, \"unit\": \"px\" } }");
        tokens.Add("\"bad\": { \"$value\": \"{missing}\" }");
        var forward = "{" + string.Join(",", tokens) + "}";
        tokens.Reverse();
        var backward = "{" + string.Join(",", tokens) + "}";
        var a = TestHelpers.Resolve(forward);
        var b = TestHelpers.Resolve(backward);
        foreach (var token in a.Tokens)
        {
            Assert.Equal(token.ResolvedValue?.ToJson(), b[token.Path]!.ResolvedValue?.ToJson());
            Assert.Equal(token.Type, b[token.Path]!.Type);
        }
        Assert.Equal(a.Diagnostics.Select(d => d.RuleId + d.TokenPath + d.Message), b.Diagnostics.Select(d => d.RuleId + d.TokenPath + d.Message));
    }

    [Fact]
    public void Reference_depth_is_limited()
    {
        var tokens = Enumerable.Range(0, 30).Select(i => $"\"t{i}\": {{ \"$value\": \"{{t{i + 1}}}\" }}").Append("\"t30\": { \"$type\": \"number\", \"$value\": 1 }");
        var set = TestHelpers.Resolve("{" + string.Join(",", tokens) + "}", new TokenResolutionOptions { MaxReferenceDepth = 10 });
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.ReferenceDepthExceeded.Id);
        Assert.False(set["t0"]!.IsValid);
        Assert.True(set["t25"]!.IsValid);
    }

    [Fact]
    public void Type_mismatch_on_alias_with_explicit_type()
    {
        var set = TestHelpers.Resolve("""{ "n": { "$type": "number", "$value": 1 }, "c": { "$type": "color", "$value": "{n}" } }""");
        var d = set.Diagnostics.Single(DiagnosticCatalog.ReferenceTypeMismatch.Id);
        Assert.Equal("color", d.Expected);
        Assert.Equal("number", d.Actual);
    }

    [Fact]
    public void Type_mismatch_inside_composite()
    {
        var set = TestHelpers.Resolve("""
        {
          "n": { "$type": "number", "$value": 1 },
          "b": { "$type": "border", "$value": { "color": "{n}", "width": { "value": 1, "unit": "px" }, "style": "solid" } }
        }
        """);
        Assert.Equal("color", set.Diagnostics.Single(DiagnosticCatalog.ReferenceTypeMismatch.Id).Expected);
    }

    [Fact]
    public void Alias_type_takes_precedence_over_group_type()
    {
        var set = TestHelpers.Resolve("""{ "n": { "$type": "number", "$value": 1 }, "g": { "$type": "dimension", "alias": { "$value": "{n}" } } }""");
        set.AssertNoErrors();
        Assert.Equal(TokenTypes.Number, set["g.alias"]!.Type);
    }

    [Fact]
    public void Undeterminable_type_is_an_error()
    {
        var set = TestHelpers.Resolve("""{ "a": { "$value": 1 } }""");
        Assert.Equal(DiagnosticSeverity.Error, set.Diagnostics.Single(DiagnosticCatalog.TypeUndetermined.Id).Severity);
    }

    [Theory]
    [InlineData("\"prefix {a}\"")]
    [InlineData("\"{a\"")]
    [InlineData("\"{}\"")]
    [InlineData("\"{a..b}\"")]
    public void Malformed_references_are_reported(string value)
    {
        var set = TestHelpers.Resolve($$"""{ "a": { "$type": "number", "$value": 1 }, "b": { "$type": "number", "$value": {{value}} } }""");
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidReferenceSyntax.Id);
    }

    [Fact]
    public void Pointer_errors_are_actionable()
    {
        var set = TestHelpers.Resolve("""
        {
          "c": { "$type": "color", "$value": { "colorSpace": "srgb", "components": [0, 0, 1] } },
          "noValue": { "$type": "color", "$ref": "#/c" },
          "badPath": { "$type": "number", "$ref": "#/c/$value/components/9" },
          "group": { "$type": "number", "$ref": "#/g/$value" },
          "g": { "x": { "$type": "number", "$value": 1 } }
        }
        """);
        var pointers = set.Diagnostics.Where(d => d.RuleId == DiagnosticCatalog.InvalidJsonPointer.Id).ToList();
        Assert.Contains(pointers, d => d.TokenPath == "noValue" && d.Expected == "#/c/$value");
        Assert.Contains(pointers, d => d.TokenPath == "badPath");
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.ReferenceTargetsGroup.Id && d.TokenPath == "group");
    }

    [Fact]
    public void Deprecation_and_type_inherit_from_groups_and_can_be_overridden()
    {
        var set = TestHelpers.Resolve("""
        {
          "old": { "$type": "number", "$deprecated": "Use new", "a": { "$value": 1 }, "b": { "$value": 2, "$deprecated": false } }
        }
        """);
        Assert.True(set["old.a"]!.IsDeprecated);
        Assert.Equal("Use new", set["old.a"]!.Deprecation!.Explanation);
        Assert.False(set["old.b"]!.IsDeprecated);
        Assert.Equal(TokenTypes.Number, set["old.b"]!.Type);
    }

    [Fact]
    public void Extends_merges_groups_recursively_and_replaces_tokens()
    {
        var set = TestHelpers.Resolve("""
        {
          "base": {
            "$type": "dimension",
            "pad": { "x": { "$value": { "value": 1, "unit": "px" } }, "y": { "$value": { "value": 2, "unit": "px" } } },
            "gap": { "$value": { "value": 3, "unit": "px" }, "$description": "base gap" }
          },
          "derived": {
            "$extends": "#/base",
            "pad": { "y": { "$value": { "value": 20, "unit": "px" } } },
            "gap": { "$value": { "value": 30, "unit": "px" } }
          }
        }
        """);
        set.AssertNoErrors();
        Assert.Equal(1, ((DimensionValue)set["derived.pad.x"]!.Value!).Value);
        Assert.Equal(20, ((DimensionValue)set["derived.pad.y"]!.Value!).Value);
        // Tokens are replaced as a whole: the description of base.gap is not merged into derived.gap.
        Assert.Null(set["derived.gap"]!.Description);
    }

    [Theory]
    [InlineData("""{ "a": { "$extends": "{b}" }, "b": { "$extends": "{a}" } }""")]
    [InlineData("""{ "a": { "inner": { "$extends": "{a}" } } }""")]
    [InlineData("""{ "a": { "$extends": "{a}" } }""")]
    public void Circular_extends_is_detected(string json)
    {
        var set = TestHelpers.Resolve(json);
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.CircularExtends.Id);
    }

    [Theory]
    [InlineData("""{ "t": { "$type": "number", "$value": 1 }, "g": { "$extends": "{t}" } }""")]
    [InlineData("""{ "g": { "$extends": "{missing}" } }""")]
    [InlineData("""{ "g": { "$extends": "button" } }""")]
    public void Invalid_extends_targets_are_reported(string json)
        => Assert.Contains(TestHelpers.Resolve(json).Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidExtends.Id);

    [Fact]
    public void Extends_can_target_a_group_that_exists_only_through_extension()
    {
        var set = TestHelpers.Resolve("""
        {
          "base": { "size": { "$type": "dimension", "sm": { "$value": { "value": 4, "unit": "px" } } } },
          "brand": { "$extends": "{base}" },
          "button": { "$extends": "{brand.size}", "lg": { "$value": { "value": 8, "unit": "px" } } },
          "chain": { "$extends": "#/button" }
        }
        """);
        set.AssertNoErrors();
        Assert.Equal(4, ((DimensionValue)set["brand.size.sm"]!.Value!).Value);
        Assert.Equal(4, ((DimensionValue)set["button.sm"]!.Value!).Value);
        Assert.Equal(8, ((DimensionValue)set["chain.lg"]!.Value!).Value);
        Assert.Equal(4, ((DimensionValue)set["chain.sm"]!.Value!).Value);
    }

    [Fact]
    public void Extends_through_inherited_groups_that_never_terminate_is_circular()
    {
        // a.x exists only because a extends b, and b extends a.x: the chain keeps growing.
        var set = TestHelpers.Resolve("""{ "a": { "$extends": "{b}" }, "b": { "$extends": "{a.x}", "x": { "t": { "$type": "number", "$value": 1 } } } }""");
        Assert.Contains(set.Diagnostics, d => d.RuleId == DiagnosticCatalog.CircularExtends.Id && d.TokenPath == "b");
    }

    [Fact]
    public void Resolution_supports_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var doc = DtcgReader.Read(Chain);
        Assert.Throws<OperationCanceledException>(() => TokenResolver.Resolve(doc, null, cts.Token));
    }

    [Fact]
    public void Merger_replaces_tokens_and_merges_groups()
    {
        var lower = (SourceObject)SourceJsonParser.Parse("""{ "g": { "$type": "number", "a": { "$value": 1 }, "b": { "$value": 2 } } }""").Root!;
        var upper = (SourceObject)SourceJsonParser.Parse("""{ "g": { "b": { "$value": 3, "$description": "x" }, "c": { "$value": 4 } } }""").Root!;
        var set = TokenResolver.Resolve(DtcgReader.Read(TokenTreeMerger.Merge(lower, upper)));
        set.AssertNoErrors();
        Assert.Equal([1d, 3d, 4d], set.Tokens.Select(t => ((NumberValue)t.Value!).Value));
        Assert.Equal("x", set["g.b"]!.Description);
    }
}
