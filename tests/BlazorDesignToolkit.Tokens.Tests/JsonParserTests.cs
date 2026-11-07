using System.Text;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Tests;

public class JsonParserTests
{
    [Fact]
    public void Reports_line_and_column_of_syntax_errors()
    {
        var result = SourceJsonParser.Parse("{\n  \"a\": 1,\n  \"b\": ]\n}", new JsonParseOptions { SourceName = "x.json" });
        Assert.Null(result.Root);
        var d = result.Diagnostics.Single(DiagnosticCatalog.InvalidJson.Id);
        Assert.Equal("x.json", d.Location!.Source);
        Assert.Equal(3, d.Location.Line);
    }

    [Fact]
    public void Records_locations_of_nodes_in_characters()
    {
        var root = (SourceObject)SourceJsonParser.Parse("{\n  \"név\": { \"x\": true }\n}").Root!;
        var prop = root.GetPropertyEntry("név")!;
        Assert.Equal(2, prop.NameLocation!.Line);
        Assert.Equal(3, prop.NameLocation.Column);
        Assert.Equal(10, prop.Value.Location!.Column); // counts 'é' as one character
    }

    [Fact]
    public void Duplicate_keys_are_reported_and_last_value_wins()
    {
        var result = SourceJsonParser.Parse("""{ "a": 1, "a": 2 }""");
        Assert.Equal(2, ((SourceNumber)((SourceObject)result.Root!)["a"]!).Value);
        Assert.Equal(DiagnosticSeverity.Error, result.Diagnostics.Single(DiagnosticCatalog.DuplicateKey.Id).Severity);
    }

    [Fact]
    public void Enforces_maximum_depth()
    {
        var json = new string('[', 30) + new string(']', 30);
        var result = SourceJsonParser.Parse(json, new JsonParseOptions { MaxDepth = 10 });
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.NestingTooDeep.Id);
    }

    [Fact]
    public void Deeply_nested_input_never_overflows_the_stack()
    {
        var json = new string('[', 100_000) + new string(']', 100_000);
        var result = SourceJsonParser.Parse(json);
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.NestingTooDeep.Id);
    }

    [Fact]
    public void Enforces_maximum_input_size()
    {
        var result = SourceJsonParser.Parse("{\"a\": \"" + new string('x', 2000) + "\"}", new JsonParseOptions { MaxInputBytes = 1000 });
        Assert.Null(result.Root);
        Assert.Equal(DiagnosticCatalog.InputTooLarge.Id, Assert.Single(result.Diagnostics).RuleId);
    }

    [Fact]
    public async Task Stream_input_stops_reading_at_the_size_limit()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Repeat("1", 100_000)) + "]"));
        var result = await SourceJsonParser.ParseAsync(stream, new JsonParseOptions { MaxInputBytes = 1024 }, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticCatalog.InputTooLarge.Id, Assert.Single(result.Diagnostics).RuleId);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_rejected_by_default_and_accepted_when_enabled()
    {
        const string json = "{ // note\n \"a\": 1, }";
        Assert.Null(SourceJsonParser.Parse(json).Root);
        Assert.NotNull(SourceJsonParser.Parse(json, new JsonParseOptions { AllowCommentsAndTrailingCommas = true }).Root);
    }

    [Fact]
    public void Skips_utf8_bom_and_rejects_empty_input()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{}")).ToArray();
        Assert.NotNull(SourceJsonParser.Parse(bytes).Root);
        Assert.Contains(SourceJsonParser.Parse("").Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidJson.Id);
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var json = "[" + string.Join(",", Enumerable.Repeat("{\"a\":1}", 5000)) + "]";
        Assert.Throws<OperationCanceledException>(() => SourceJsonParser.Parse(json, null, cts.Token));
    }

    [Fact]
    public void Writer_is_deterministic_and_preserves_number_text()
    {
        var node = SourceJsonParser.Parse("""{"b":1.50,"a":[1e2,"x"],"c":{"d":null}}""").Root!;
        var json = node.ToJson(indented: true);
        Assert.Equal("{\n  \"b\": 1.50,\n  \"a\": [\n    100,\n    \"x\"\n  ],\n  \"c\": {\n    \"d\": null\n  }\n}", json);
        Assert.Equal(json, SourceJsonParser.Parse(json).Root!.ToJson(indented: true));
    }

    [Theory]
    [InlineData("#/a/b", new[] { "a", "b" })]
    [InlineData("#/a~1b/c~0d", new[] { "a/b", "c~d" })]
    [InlineData("#/brand colors/$value/components/0", new[] { "brand colors", "$value", "components", "0" })]
    public void Parses_json_pointers(string pointer, string[] expected)
    {
        Assert.True(JsonPointer.TryParse(pointer, out var segments, out _));
        Assert.Equal(expected, segments);
        Assert.Equal(pointer, JsonPointer.Format(segments));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("#a")]
    [InlineData("#/a~2")]
    public void Rejects_invalid_pointers(string pointer) => Assert.False(JsonPointer.TryParse(pointer, out _, out _));
}
