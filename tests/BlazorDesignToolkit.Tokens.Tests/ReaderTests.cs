using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Tests;

public class ReaderTests
{
    [Theory]
    [InlineData("$bad")]
    [InlineData("a.b")]
    [InlineData("a{b")]
    [InlineData("a}b")]
    public void Rejects_invalid_names(string name)
    {
        var doc = DtcgReader.Read($$"""{ "{{name}}": { "$type": "number", "$value": 1 } }""");
        if (name.StartsWith('$')) Assert.Contains(doc.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnknownReservedProperty.Id);
        else Assert.Contains(doc.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidName.Id);
        Assert.Empty(doc.Root.EnumerateTokens());
    }

    [Fact]
    public void Accepts_names_with_spaces_unicode_and_digits()
    {
        var doc = DtcgReader.Read("""{ "brand colors": { "100": { "$type": "number", "$value": 1 }, "ünï": { "$type": "number", "$value": 2 } } }""");
        Assert.Empty(doc.Diagnostics);
        Assert.Equal(["brand colors.100", "brand colors.ünï"], doc.Root.EnumerateTokens().Select(t => t.Path.ToString()));
    }

    [Fact]
    public void Token_with_children_is_an_error()
    {
        var doc = DtcgReader.Read("""{ "a": { "$type": "number", "$value": 1, "child": { "$value": 2 } } }""");
        Assert.Equal(DiagnosticSeverity.Error, doc.Diagnostics.Single(DiagnosticCatalog.TokenWithChildren.Id).Severity);
    }

    [Fact]
    public void Type_names_are_case_sensitive_with_a_suggestion()
    {
        var doc = DtcgReader.Read("""{ "a": { "$type": "Color", "$value": {} } }""");
        var d = doc.Diagnostics.Single(DiagnosticCatalog.UnknownType.Id);
        Assert.Contains("'color'", d.Remediation);
    }

    [Fact]
    public void Validates_reserved_property_types()
    {
        var doc = DtcgReader.Read("""{ "g": { "$description": 1, "$extensions": [], "$deprecated": 3, "t": { "$type": 5, "$value": 1 } } }""");
        Assert.Equal(4, doc.Diagnostics.Count(d => d.RuleId == DiagnosticCatalog.InvalidPropertyType.Id));
    }

    [Fact]
    public void Unknown_reserved_properties_are_preserved_with_a_warning()
    {
        const string json = """{ "t": { "$type": "number", "$value": 1, "$future": { "x": 1 } } }""";
        var doc = DtcgReader.Read(json);
        Assert.Equal(DiagnosticSeverity.Warning, doc.Diagnostics.Single(DiagnosticCatalog.UnknownReservedProperty.Id).Severity);
        var token = doc.Root.EnumerateTokens().Single();
        Assert.Equal("$future", Assert.Single(token.UnknownReservedProperties).Name);
        Assert.Contains("$future", SourceJsonWriter.ToJsonString(DtcgWriter.ToSourceObject(doc.Root)));
    }

    [Fact]
    public void Schema_at_root_is_tolerated()
    {
        var doc = DtcgReader.Read("""{ "$schema": "https://example.com/s.json", "a": { "$type": "number", "$value": 1 } }""");
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Deprecated_accepts_boolean_and_string()
    {
        var doc = DtcgReader.Read("""{ "a": { "$type": "number", "$value": 1, "$deprecated": "Use b" }, "b": { "$type": "number", "$value": 1, "$deprecated": false } }""");
        var tokens = doc.Root.EnumerateTokens().ToList();
        Assert.Equal(new TokenDeprecation(true, "Use b"), tokens[0].Deprecation);
        Assert.Equal(TokenDeprecation.NotDeprecated, tokens[1].Deprecation);
    }

    [Fact]
    public void Root_of_document_must_be_an_object()
    {
        var doc = DtcgReader.Read("[1, 2]");
        Assert.Equal(DiagnosticCatalog.RootNotObject.Id, Assert.Single(doc.Diagnostics).RuleId);
    }

    [Fact]
    public void Names_differing_only_by_case_produce_info()
    {
        var doc = DtcgReader.Read("""{ "font-size": { "$type": "number", "$value": 1 }, "FONT-SIZE": { "$type": "number", "$value": 2 } }""");
        Assert.Equal(DiagnosticSeverity.Info, doc.Diagnostics.Single(DiagnosticCatalog.NamesDifferOnlyByCase.Id).Severity);
        Assert.Equal(2, doc.Root.EnumerateTokens().Count());
    }

    [Fact]
    public void Extends_on_token_is_invalid()
    {
        var doc = DtcgReader.Read("""{ "a": { "$type": "number", "$value": 1, "$extends": "{b}" } }""");
        Assert.Contains(doc.Diagnostics, d => d.RuleId == DiagnosticCatalog.InvalidExtends.Id);
    }

    [Fact]
    public void Round_trip_preserves_document_and_extensions()
    {
        var json = TestHelpers.ReadFixture("conformance", "all-types.tokens.json");
        var doc = DtcgReader.Read(json);
        var written = DtcgWriter.Write(doc);
        Assert.True(SourceNode.DeepEquals(SourceJsonParser.Parse(json).Root, SourceJsonParser.Parse(written).Root));

        var rebuilt = SourceJsonWriter.ToJsonString(DtcgWriter.ToSourceObject(doc.Root));
        Assert.True(SourceNode.DeepEquals(SourceJsonParser.Parse(json).Root, SourceJsonParser.Parse(rebuilt).Root));
    }

    [Fact]
    public void Extensions_survive_round_trip()
    {
        const string json = """{ "a": { "$type": "number", "$value": 1, "$extensions": { "com.example.tool": { "nested": [1, { "x": "y" }] } } } }""";
        var doc = DtcgReader.Read(json);
        var output = SourceJsonWriter.ToJsonString(DtcgWriter.ToSourceObject(doc.Root));
        Assert.True(SourceNode.DeepEquals(SourceJsonParser.Parse(json).Root, SourceJsonParser.Parse(output).Root));
    }
}
