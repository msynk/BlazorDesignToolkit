using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(BlazorDesignToolkit.BrowserTests.DemoServer))]

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>
/// Hosts the demo application on a real Kestrel port and owns one Playwright browser for the test run.
/// Set BDT_BROWSER to chromium (default), firefox or webkit.
/// </summary>
public sealed class DemoServer : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private IPlaywright? _playwright;
    private readonly List<IBrowserContext> _contexts = [];

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = default!;
    public string BrowserName { get; } = Environment.GetEnvironmentVariable("BDT_BROWSER") ?? "chromium";

    public async ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        _factory.UseKestrel(0);
        _factory.StartServer();
        BaseUrl = _factory.ClientOptions.BaseAddress.ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        var type = BrowserName switch
        {
            "firefox" => _playwright.Firefox,
            "webkit" => _playwright.Webkit,
            _ => _playwright.Chromium,
        };
        try
        {
            Browser = await type.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The Playwright {BrowserName} browser is not installed. Run: pwsh tests/BlazorDesignToolkit.BrowserTests/bin/Debug/net10.0/playwright.ps1 install {BrowserName}", ex);
        }
    }

    /// <summary>A fresh, isolated browser context (cookies, storage, emulation) per test. Closed by <see cref="CloseContextsAsync"/>.</summary>
    public async Task<IPage> NewPageAsync(ColorScheme? colorScheme = null, Contrast? contrast = null, int width = 1280, int height = 900, ReducedMotion? reducedMotion = null)
    {
        var context = await NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme ?? ColorScheme.Light,
            Contrast = contrast ?? Contrast.NoPreference,
            ReducedMotion = reducedMotion ?? ReducedMotion.NoPreference,
        });
        context.SetDefaultTimeout(15000);
        return await context.NewPageAsync();
    }

    /// <summary>Creates a browser context that is closed after the current test.</summary>
    public async Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options)
    {
        var context = await Browser.NewContextAsync(options);
        lock (_contexts) _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Closes the contexts opened by the current test (tests run sequentially, see xunit.runner.json). Leaving them open
    /// keeps their Blazor circuits and WebAssembly runtimes alive for the rest of the run.
    /// </summary>
    public async Task CloseContextsAsync()
    {
        IBrowserContext[] contexts;
        lock (_contexts) { contexts = [.. _contexts]; _contexts.Clear(); }
        foreach (var context in contexts)
        {
            try { await context.CloseAsync(); }
            catch (PlaywrightException) { /* the browser may already have discarded it */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }
}

internal static class PageExtensions
{
    /// <summary>Navigates and waits until the interactive renderer (Server circuit or WebAssembly runtime) has taken over.</summary>
    public static async Task OpenAsync(this IPage page, string path, string mode = "server")
    {
        var separator = path.Contains('?') ? '&' : '?';
        await page.GotoAsync($"{path}{separator}mode={mode}");
        var expected = mode == "wasm" ? "WebAssembly" : "Server";
        await page.Locator(".site-footer strong").GetByText(expected, new LocatorGetByTextOptions { Exact = true }).WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        // FocusOnNavigate moves focus to the page heading after the first interactive render. Wait for it, or it can steal
        // focus from an element the test has just focused and send the following key presses to the heading.
        await page.WaitForFunctionAsync("() => document.activeElement?.tagName === 'H1'", null, new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    public static Task<string> ActiveDescriptionAsync(this IPage page)
        => page.EvaluateAsync<string>("() => { const e = document.activeElement; return e ? (e.getAttribute('data-testid') || e.getAttribute('role') || e.tagName.toLowerCase()) + '|' + (e.textContent || '').trim() : ''; }");

    public static Task<string> ActiveIdAsync(this IPage page) => page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");

    public static Task<string> CssVariableAsync(this IPage page, string name)
        => page.EvaluateAsync<string>($"() => getComputedStyle(document.documentElement).getPropertyValue('{name}').trim()");
}
