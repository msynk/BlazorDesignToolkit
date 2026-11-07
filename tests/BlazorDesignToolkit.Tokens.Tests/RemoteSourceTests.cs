using System.Net;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tests;

/// <summary>Opt-in remote sources (HttpResolverSourceLoader) and same-document pointers in resolver sources.</summary>
public class RemoteSourceTests
{
    private const string Tokens = """{ "space": { "$type": "number", "$value": 4 } }""";
    private static readonly Uri s_allowed = new("https://tokens.example.com/design/");

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            await Task.Yield();
            var response = respond(request);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static (HttpResolverSourceLoader Loader, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, RemoteSourceOptions? options = null,
        IResolverSourceLoader? local = null)
    {
        var handler = new FakeHandler(respond);
        return (new HttpResolverSourceLoader(new HttpClient(handler), options ?? new RemoteSourceOptions { AllowedPrefixes = [s_allowed] }, local), handler);
    }

    private static Task<SourceParseResult> Load(HttpResolverSourceLoader loader, string reference)
        => loader.LoadAsync(reference, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Allow_listed_https_urls_are_fetched_and_parsed()
    {
        var (loader, handler) = Create(_ => Json(Tokens));
        var result = await Load(loader, "https://tokens.example.com/design/base.tokens.json");
        Assert.IsType<SourceObject>(result.Root);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("https://tokens.example.com/other/base.tokens.json")]     // outside the prefix path
    [InlineData("https://evil.example.com/design/base.tokens.json")]      // other host
    [InlineData("http://tokens.example.com/design/base.tokens.json")]     // not https
    [InlineData("https://user:pw@tokens.example.com/design/x.json")]      // credentials in the URL
    [InlineData("https://tokens.example.com/designer/x.json")]            // prefix must match a path boundary as written
    public async Task Urls_outside_the_allow_list_are_never_requested(string url)
    {
        var (loader, handler) = Create(_ => Json(Tokens));
        var result = await Load(loader, url);
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.UnsupportedConstruct.Id);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Redirects_are_followed_only_to_allow_listed_urls()
    {
        var (loader, handler) = Create(request => request.RequestUri!.AbsolutePath.EndsWith("/a.json", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/design/b.json", UriKind.Relative) } }
            : request.RequestUri!.AbsolutePath.EndsWith("/b.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example.com/c.json") } }
                : Json(Tokens));
        var result = await Load(loader, "https://tokens.example.com/design/a.json");
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("not allow-listed", StringComparison.Ordinal));
        Assert.Equal(2, handler.Requests.Count); // a.json and b.json; the off-list target is not requested
    }

    [Fact]
    public async Task Downloads_are_limited_in_size()
    {
        var options = new RemoteSourceOptions { AllowedPrefixes = [s_allowed], ParseOptions = JsonParseOptions.Default with { MaxInputBytes = 64 } };
        var (loader, _) = Create(_ => Json("{ \"a\": \"" + new string('x', 200) + "\" }"), options);
        var result = await Load(loader, "https://tokens.example.com/design/big.json");
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, d => d.RuleId == DiagnosticCatalog.InputTooLarge.Id);
    }

    [Fact]
    public async Task Http_errors_and_timeouts_become_diagnostics()
    {
        var (notFound, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Contains((await Load(notFound, "https://tokens.example.com/design/x.json")).Diagnostics, d => d.Message.Contains("HTTP 404", StringComparison.Ordinal));

        var slow = new HttpResolverSourceLoader(new HttpClient(new SlowHandler()), new RemoteSourceOptions { AllowedPrefixes = [s_allowed], Timeout = TimeSpan.FromMilliseconds(50) });
        Assert.Contains((await Load(slow, "https://tokens.example.com/design/x.json")).Diagnostics, d => d.Message.Contains("timed out", StringComparison.Ordinal));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Json(Tokens);
        }
    }

    [Fact]
    public async Task Relative_references_go_to_the_local_loader()
    {
        var local = new InMemoryResolverSourceLoader(new Dictionary<string, string> { ["local.json"] = Tokens });
        var (loader, handler) = Create(_ => Json(Tokens), local: local);
        Assert.IsType<SourceObject>((await Load(loader, "local.json")).Root);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Resolver_documents_can_mix_remote_and_local_sources()
    {
        var local = new InMemoryResolverSourceLoader(new Dictionary<string, string> { ["dark.json"] = """{ "space": { "$type": "number", "$value": 8 } }""" });
        var (loader, _) = Create(_ => Json(Tokens), local: local);
        var doc = DtcgResolver.Read("""
        {
          "version": "2025.10",
          "sets": { "base": { "sources": [{ "$ref": "https://tokens.example.com/design/base.tokens.json" }, { "$ref": "dark.json" }] } },
          "resolutionOrder": [{ "$ref": "#/sets/base" }]
        }
        """);
        var result = await DtcgResolver.ResolveAsync(doc, new Dictionary<string, string>(), loader, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(8, ((NumberValue)result.Resolved["space"]!.Value!).Value);
    }

    [Fact]
    public async Task File_loader_rejects_remote_references_by_default()
    {
        var loader = new FileSystemResolverSourceLoader(AppContext.BaseDirectory);
        var result = await loader.LoadAsync("https://tokens.example.com/design/x.json", null, TestContext.Current.CancellationToken);
        Assert.Null(result.Root);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("off by default", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_remote_options_are_reported()
    {
        Assert.NotNull(new RemoteSourceOptions { AllowedPrefixes = [new Uri("http://x.example.com/")] }.Validate());
        Assert.NotNull(new RemoteSourceOptions { AllowedPrefixes = [new Uri("https://x.example.com/?a=1")] }.Validate());
        Assert.Null(new RemoteSourceOptions { AllowedPrefixes = [s_allowed] }.Validate());
    }

    [Fact]
    public async Task Same_document_pointers_in_sources_resolve_against_the_resolver_document()
    {
        var doc = DtcgResolver.Read("""
        {
          "version": "2025.10",
          "$defs": {
            "spacing": { "space": { "$type": "number", "$value": 4 } },
            "both": [{ "$ref": "#/$defs/spacing" }, { "radius": { "$type": "number", "$value": 2 } }]
          },
          "sets": { "base": { "sources": [{ "$ref": "#/$defs/both" }] }, "loop": { "sources": [{ "$ref": "#/sets/loop/sources" }] } },
          "resolutionOrder": [{ "$ref": "#/sets/base" }]
        }
        """);
        var loader = new InMemoryResolverSourceLoader(new Dictionary<string, string>());
        var result = await DtcgResolver.ResolveAsync(doc, new Dictionary<string, string>(), loader, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(4, ((NumberValue)result.Resolved["space"]!.Value!).Value);
        Assert.Equal(2, ((NumberValue)result.Resolved["radius"]!.Value!).Value);

        var session = new ResolverSession(doc, loader);
        var bag = new DiagnosticBag();
        await session.GetSetLayersAsync(doc.FindSet("loop")!, bag, TestContext.Current.CancellationToken);
        Assert.Contains(bag.Items, d => d.Message.Contains("Circular", StringComparison.Ordinal));
    }
}
