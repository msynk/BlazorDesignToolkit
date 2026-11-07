using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

/// <summary>DTCG 2025.10 resolver module: sets, modifiers, inputs and resolution order.</summary>
public class ResolverModuleTests
{
    private static readonly Dictionary<string, string> s_files = new()
    {
        ["base.json"] = """{ "color": { "$type": "color", "white": { "$value": { "colorSpace": "srgb", "components": [1,1,1] } }, "black": { "$value": { "colorSpace": "srgb", "components": [0,0,0] } } } }""",
        ["light.json"] = """{ "color": { "bg": { "$type": "color", "$value": "{color.white}" }, "fg": { "$type": "color", "$value": "{color.black}" } } }""",
        ["dark.json"] = """{ "color": { "bg": { "$type": "color", "$value": "{color.black}" }, "fg": { "$type": "color", "$value": "{color.white}" } } }""",
    };

    private const string Doc = """
    {
      "version": "2025.10",
      "sets": { "base": { "sources": [{ "$ref": "base.json" }] } },
      "modifiers": {
        "theme": { "contexts": { "light": [{ "$ref": "light.json" }], "dark": [{ "$ref": "dark.json" }] }, "default": "light" },
        "size": { "contexts": { "small": [{ "space": { "$type": "number", "$value": 4 } }], "large": [{ "space": { "$type": "number", "$value": 8 } }] } }
      },
      "resolutionOrder": [{ "$ref": "#/sets/base" }, { "$ref": "#/modifiers/theme" }, { "$ref": "#/modifiers/size" }]
    }
    """;

    private static Task<ResolverResult> Resolve(string doc, Dictionary<string, string> inputs)
        => DtcgResolver.ResolveAsync(DtcgResolver.Read(doc, "theme.resolver.json"), inputs, new InMemoryResolverSourceLoader(s_files), cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task Resolves_selected_contexts_in_order_and_aliases_after_merge()
    {
        var result = await Resolve(Doc, new() { ["theme"] = "dark", ["size"] = "large" });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal("#000000", ((ColorValue)result.Resolved["color.bg"]!.Value!).ToSrgb().ToHex());
        Assert.Equal(8, ((NumberValue)result.Resolved["space"]!.Value!).Value);
    }

    [Fact]
    public async Task Defaults_apply_and_inputs_are_case_insensitive()
    {
        var result = await Resolve(Doc, new() { ["SIZE"] = "Small" });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal("light", result.Inputs["theme"]);
        Assert.Equal("small", result.Inputs["size"]);
    }

    [Fact]
    public async Task Invalid_inputs_report_every_problem()
    {
        var result = await Resolve(Doc, new() { ["theme"] = "blue", ["foo"] = "bar" });
        var messages = result.Diagnostics.Where(d => d.RuleId == DiagnosticCatalog.InvalidResolverInput.Id).Select(d => d.Message).ToList();
        Assert.Contains("Invalid context \"blue\" for modifier \"theme\".", messages);
        Assert.Contains("Unknown modifier \"foo\".", messages);
        Assert.Contains("Missing modifier \"size\".", messages);
    }

    [Theory]
    [InlineData("""{ "resolutionOrder": [] }""", "version")]
    [InlineData("""{ "version": "2024.01", "resolutionOrder": [] }""", "Unsupported resolver version")]
    [InlineData("""{ "version": "2025.10" }""", "resolutionOrder")]
    [InlineData("""{ "version": "2025.10", "modifiers": { "m": { "contexts": {} } }, "resolutionOrder": [] }""", "empty")]
    [InlineData("""{ "version": "2025.10", "modifiers": { "m": { "contexts": { "a": [], "b": [] }, "default": "c" } }, "resolutionOrder": [] }""", "default")]
    [InlineData("""{ "version": "2025.10", "resolutionOrder": [{ "type": "set", "sources": [] }] }""", "name")]
    [InlineData("""{ "version": "2025.10", "sets": { "a": { "sources": [{ "$ref": "#/modifiers/x" }] } }, "resolutionOrder": [] }""", "only resolutionOrder may reference modifiers")]
    [InlineData("""{ "version": "2025.10", "sets": { "a": { "sources": [] } }, "resolutionOrder": [{ "$ref": "#/sets/a" }, { "$ref": "#/sets/a" }] }""", "more than once")]
    public void Invalid_resolver_documents_are_rejected(string json, string messagePart)
    {
        var doc = DtcgResolver.Read(json);
        Assert.Contains(doc.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Single_context_modifier_warns_and_defs_are_ignored()
    {
        var doc = DtcgResolver.Read("""{ "version": "2025.10", "$defs": { "x": 1 }, "modifiers": { "m": { "contexts": { "only": [] } } }, "resolutionOrder": [{ "$ref": "#/modifiers/m" }] }""");
        Assert.Equal(DiagnosticSeverity.Warning, doc.Diagnostics.Single(DiagnosticCatalog.ResolverSingleContext.Id).Severity);
        Assert.False(doc.HasErrors);
    }

    [Fact]
    public async Task Inline_sets_and_set_references_inside_contexts()
    {
        const string doc = """
        {
          "version": "2025.10",
          "sets": { "shared": { "sources": [{ "n": { "$type": "number", "$value": 1 } }] } },
          "modifiers": { "m": { "contexts": { "a": [{ "$ref": "#/sets/shared" }], "b": [] }, "default": "a" } },
          "resolutionOrder": [
            { "name": "inline", "type": "set", "sources": [{ "n": { "$type": "number", "$value": 0 } }] },
            { "$ref": "#/modifiers/m" }
          ]
        }
        """;
        var a = await Resolve(doc, []);
        Assert.Equal(1, ((NumberValue)a.Resolved["n"]!.Value!).Value);
        var b = await Resolve(doc, new() { ["m"] = "b" });
        Assert.Equal(0, ((NumberValue)b.Resolved["n"]!.Value!).Value);
    }

    [Fact]
    public async Task Reference_siblings_override_shallowly()
    {
        const string doc = """
        { "version": "2025.10",
          "sets": { "s": { "sources": [{ "$ref": "base.json", "color": { "only": { "$type": "number", "$value": 1 } } }] } },
          "resolutionOrder": [{ "$ref": "#/sets/s" }] }
        """;
        var result = await Resolve(doc, []);
        Assert.Equal(["color.only"], result.Resolved.Tokens.Select(t => t.Path.ToString()));
    }

    [Fact]
    public async Task Circular_set_references_are_detected()
    {
        const string doc = """
        { "version": "2025.10",
          "sets": { "a": { "sources": [{ "$ref": "#/sets/b" }] }, "b": { "sources": [{ "$ref": "#/sets/a" }] } },
          "resolutionOrder": [{ "$ref": "#/sets/a" }] }
        """;
        var result = await Resolve(doc, []);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Circular set reference"));
    }

    [Fact]
    public async Task File_loader_refuses_paths_outside_root_and_remote_urls()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdt-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var loader = new FileSystemResolverSourceLoader(root);
            var ct = TestContext.Current.CancellationToken;
            Assert.Contains("outside the allowed root", Assert.Single((await loader.LoadAsync("../secret.json", root, ct)).Diagnostics).Message);
            Assert.Contains("off by default", Assert.Single((await loader.LoadAsync("https://example.com/t.json", root, ct)).Diagnostics).Message);
            Assert.Equal(DiagnosticCatalog.FileNotFound.Id, Assert.Single((await loader.LoadAsync("missing.json", root, ct)).Diagnostics).RuleId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Sample_resolver_document_resolves_every_context()
    {
        var path = TestHelpers.SamplePath("aurora.resolver.json");
        var doc = await DtcgResolver.ReadFileAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(doc.Diagnostics);
        var session = new ResolverSession(doc, new FileSystemResolverSourceLoader(Path.GetDirectoryName(path)!));
        foreach (var mode in new[] { "light", "dark", "lightMoreContrast", "darkMoreContrast" })
        {
            foreach (var density in new[] { "comfortable", "compact" })
            {
                var result = await session.ResolveAsync(new Dictionary<string, string> { ["mode"] = mode, ["density"] = density }, cancellationToken: TestContext.Current.CancellationToken);
                Assert.False(result.HasErrors, $"{mode}/{density}: " + string.Join("\n", result.Diagnostics));
            }
        }
    }
}
