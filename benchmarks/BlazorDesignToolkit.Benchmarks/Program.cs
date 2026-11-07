using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Validation;

BenchmarkSwitcher.FromAssembly(typeof(TokenSetBenchmarks).Assembly).Run(args);

/// <summary>Synthetic DTCG documents: a palette, semantic aliases (with two-level chains), dimensions and typography composites.</summary>
public static class Synthetic
{
    public static string Document(int tokens)
    {
        var sb = new StringBuilder("{\n \"palette\": { \"$type\": \"color\"");
        var colors = Math.Max(1, tokens / 4);
        for (var i = 0; i < colors; i++)
        {
            var c = (i % 255) / 255.0;
            sb.Append($",\n  \"c{i}\": {{ \"$value\": {{ \"colorSpace\": \"srgb\", \"components\": [{c.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, 0.4, 0.6] }} }}");
        }
        sb.Append(" },\n \"semantic\": { \"$type\": \"color\"");
        for (var i = 0; i < colors; i++) sb.Append($",\n  \"s{i}\": {{ \"$value\": \"{{palette.c{i}}}\" }}");
        sb.Append(" },\n \"alias\": {");
        for (var i = 0; i < colors; i++) sb.Append($"{(i == 0 ? "" : ",")}\n  \"a{i}\": {{ \"$value\": \"{{semantic.s{i}}}\" }}");
        sb.Append(" },\n \"space\": { \"$type\": \"dimension\"");
        var dims = Math.Max(1, tokens / 8);
        for (var i = 0; i < dims; i++) sb.Append($",\n  \"d{i}\": {{ \"$value\": {{ \"value\": {i * 2}, \"unit\": \"px\" }} }}");
        sb.Append(" },\n \"type\": { \"$type\": \"typography\"");
        for (var i = 0; i < dims; i++)
        {
            sb.Append($",\n  \"t{i}\": {{ \"$value\": {{ \"fontFamily\": [\"Inter\", \"sans-serif\"], \"fontSize\": \"{{space.d{i}}}\", \"fontWeight\": 400, \"letterSpacing\": {{ \"value\": 0, \"unit\": \"px\" }}, \"lineHeight\": 1.5 }} }}");
        }
        sb.Append(" }\n}");
        return sb.ToString();
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class TokenSetBenchmarks
{
    private string _json = "";
    private TokenDocument _document = default!;
    private ResolvedTokenSet _resolved = default!;

    [Params(100, 2_000, 20_000)]
    public int Tokens { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _json = Synthetic.Document(Tokens);
        _document = DtcgReader.Read(_json);
        _resolved = TokenResolver.Resolve(_document);
        if (_resolved.HasErrors) throw new InvalidOperationException(string.Join("\n", _resolved.Diagnostics.Take(5)));
    }

    [Benchmark(Description = "Parse JSON + build model")]
    public TokenDocument Parse() => DtcgReader.Read(_json);

    [Benchmark(Description = "Resolve references + validate values")]
    public ResolvedTokenSet Resolve() => TokenResolver.Resolve(_document);

    [Benchmark(Description = "Design-system rules")]
    public ValidationReport Validate() => TokenValidator.Validate(_resolved, new ValidationOptions { ReportDuplicateValues = true });

    [Benchmark(Description = "Export CSS (resolved)")]
    public string ExportCss() => CssExporter.Export(_resolved).Css;

    [Benchmark(Description = "Write DTCG JSON")]
    public string WriteJson() => DtcgWriter.Write(_resolved);
}

[MemoryDiagnoser]
[ShortRunJob]
public class ThemeBenchmarks
{
    private ThemeCatalog _catalog = default!;
    private ThemeSelection _dark = default!;
    private SourceObject _override = default!;

    [GlobalSetup]
    public void Setup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SampleTokens", "aurora.themes.json");
        _catalog = ThemeManifest.LoadAsync(path).GetAwaiter().GetResult().Catalog!;
        _dark = new ThemeSelection("ember", new Dictionary<string, string> { ["mode"] = "dark", ["contrast"] = "more" });
        _override = (SourceObject)SourceJsonParser.Parse("""{ "color": { "blue": { "600": { "$value": { "colorSpace": "srgb", "components": [1, 0, 0] } } } } }""").Root!;
    }

    [Benchmark(Description = "Compose one theme selection (uncached)")]
    public ThemeResolution Compose() => ThemeComposer.Compose(_catalog, _dark);

    [Benchmark(Description = "Generate full theme stylesheet (2 brands x 8 combinations)")]
    public string Stylesheet() => ThemeStylesheetGenerator.Generate(new ThemeCatalogBuilder().AddAxis(_catalog.Axes[0]).AddAxis(_catalog.Axes[1]).AddAxis(_catalog.Axes[2])
        .AddTheme(_catalog.Themes[0], true).AddTheme(_catalog.Themes[1]).Build()).Css;

    [Benchmark(Description = "Runtime override declarations")]
    public int OverrideDeclarations() => ThemeStylesheetGenerator.GetOverrideDeclarations(_catalog, _dark, _override, null, out _).Count;
}
