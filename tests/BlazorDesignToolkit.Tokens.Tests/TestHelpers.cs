using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;

namespace BlazorDesignToolkit.Tokens.Tests;

internal static class TestHelpers
{
    public static string FixturePath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    public static string SamplePath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "SampleTokens", .. parts]);

    public static string ReadFixture(params string[] parts) => File.ReadAllText(FixturePath(parts));

    public static ResolvedTokenSet Resolve(string json, TokenResolutionOptions? options = null)
        => TokenResolver.Resolve(json, new JsonParseOptions { SourceName = "test.tokens.json" }, options);

    public static ResolvedTokenSet ResolveFixture(params string[] parts) => Resolve(ReadFixture(parts));

    public static ResolvedTokenSet ResolveSamples(params string[] files)
    {
        var layers = files.Select(f => (SourceObject)SourceJsonParser.Parse(File.ReadAllText(SamplePath(f)), new JsonParseOptions { SourceName = f }).Root!).ToList();
        return TokenResolver.Resolve(DtcgReader.Read(TokenTreeMerger.Merge(layers)));
    }

    public static IReadOnlyList<Diagnostic> Errors(this ResolvedTokenSet set) => set.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();

    public static Diagnostic Single(this IEnumerable<Diagnostic> diagnostics, string ruleId)
    {
        var matches = diagnostics.Where(d => d.RuleId == ruleId).ToList();
        Assert.True(matches.Count == 1, $"Expected exactly one {ruleId}, found {matches.Count}:\n{string.Join("\n", diagnostics)}");
        return matches[0];
    }

    public static void AssertNoErrors(this ResolvedTokenSet set)
        => Assert.True(!set.HasErrors, "Unexpected errors:\n" + string.Join("\n", set.Errors()));

    /// <summary>Wraps a token definition into a minimal document.</summary>
    public static string Token(string type, string valueJson) => $$"""{ "t": { "$type": "{{type}}", "$value": {{valueJson}} } }""";
}
