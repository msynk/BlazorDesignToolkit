using System.Diagnostics;
using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>Runtime theme switching, system preferences, persistence and startup behavior in a real browser.</summary>
public class ThemingBrowserTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    private static async Task ChooseAsync(IPage page, string testId, string option)
    {
        await page.GetByTestId(testId).ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = option, Exact = true }).ClickAsync();
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public async Task Switching_mode_updates_attributes_and_variables_without_reload(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/themes", mode);
        await page.EvaluateAsync("() => window.__noReload = true");
        var lightSurface = await page.CssVariableAsync("--ds-color-surface-default");
        await ChooseAsync(page, "axis-mode", "Dark");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", "dark");
        var darkSurface = await page.CssVariableAsync("--ds-color-surface-default");
        Assert.NotEqual(lightSurface, darkSurface);
        Assert.Equal("dark", await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).colorScheme"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__noReload === true"), "The page reloaded.");

        await ChooseAsync(page, "theme-select", "Ember");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "ember");
        // Computed custom properties are substituted, so compare with the referenced palette step.
        Assert.Equal(await page.CssVariableAsync("--ds-color-ember-400"), await page.CssVariableAsync("--ds-color-surface-accent"));
    }

    [Fact]
    public async Task System_mode_follows_operating_system_changes()
    {
        var page = await server.NewPageAsync(ColorScheme.Light);
        await page.OpenAsync("/themes");
        await ChooseAsync(page, "axis-mode", "System");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", "system");
        var light = await page.CssVariableAsync("--ds-color-surface-default");
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
        // CSS reacts immediately through @media; .NET learns about it through the matchMedia listener.
        Assert.NotEqual(light, await page.CssVariableAsync("--ds-color-surface-default"));
        await Assertions.Expect(page.Locator(".facts")).ToContainTextAsync("mode=dark");
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Light });
        await Assertions.Expect(page.Locator(".facts")).ToContainTextAsync("mode=light");
    }

    [Fact]
    public async Task Increased_contrast_preference_applies_contrast_overrides()
    {
        var page = await server.NewPageAsync(contrast: Contrast.More);
        await page.Context.AddCookiesAsync([new Cookie { Name = "bdt-theme", Value = Uri.EscapeDataString("theme=aurora;contrast=system;mode=light"), Url = server.BaseUrl }]);
        await page.OpenAsync("/themes");
        Assert.Equal(await page.CssVariableAsync("--ds-color-neutral-900"), await page.CssVariableAsync("--ds-color-border-default"));
    }

    [Fact]
    public async Task Persisted_theme_is_rendered_by_the_server_before_any_script_runs()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/themes");
        await ChooseAsync(page, "axis-mode", "Dark");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", "dark");
        // The cookie written by the browser module is sent with the next request; the raw HTML already carries the theme.
        var response = await page.Context.APIRequest.GetAsync("/themes");
        var html = await response.TextAsync();
        Assert.Contains("data-mode=\"dark\"", html);
        Assert.Contains("<style id=\"bdt-theme-styles\"", html);

        // And a page without JavaScript at all is styled correctly.
        var noJs = await server.NewContextAsync(new BrowserNewContextOptions { BaseURL = server.BaseUrl, JavaScriptEnabled = false });
        await noJs.AddCookiesAsync([new Cookie { Name = "bdt-theme", Value = Uri.EscapeDataString("theme=ember;mode=dark"), Url = server.BaseUrl }]);
        var staticPage = await noJs.NewPageAsync();
        await staticPage.GotoAsync("/");
        Assert.Equal("dark", await staticPage.Locator("html").GetAttributeAsync("data-mode"));
        Assert.Equal("ember", await staticPage.Locator("html").GetAttributeAsync("data-theme"));
    }

    [Fact]
    public async Task Live_override_updates_inline_custom_property()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/editor");
        var combo = page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = "Token" });
        await combo.FillAsync("color.blue.600");
        await page.Keyboard.PressAsync("Enter");
        var input = page.Locator("input[type=color]");
        await input.EvaluateAsync("(el) => { el.value = '#ff0000'; el.dispatchEvent(new Event('change', { bubbles: true })); }");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("style", new System.Text.RegularExpressions.Regex("--ds-color-blue-600: #ff0000"));
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Clear overrides" }).ClickAsync();
        await Assertions.Expect(page.Locator("html")).Not.ToHaveAttributeAsync("style", new System.Text.RegularExpressions.Regex("--ds-color-blue-600"));
    }

    [Fact]
    public async Task Theme_switch_latency_is_recorded()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/themes");
        var timings = new List<double>();
        foreach (var value in new[] { "Dark", "Light", "Dark", "Light", "Dark" })
        {
            var watch = Stopwatch.StartNew();
            await ChooseAsync(page, "axis-mode", value);
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", value.ToLowerInvariant());
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }
        // Includes two clicks and a server round trip each; recorded for the performance baseline, generous bound only.
        TestContext.Current.SendDiagnosticMessage($"Theme switch latency (ms, click to attribute applied, Interactive Server): {string.Join(", ", timings.Select(t => t.ToString("0")))}");
        Assert.True(timings.Average() < 3000, $"Average theme switch took {timings.Average():0} ms");
    }

    [Fact]
    public async Task Layout_reflows_at_320_css_pixels_without_horizontal_scrolling()
    {
        var page = await server.NewPageAsync(width: 320, height: 640);
        foreach (var path in new[] { "/", "/primitives/tabs", "/primitives/listbox", "/design-system" })
        {
            await page.OpenAsync(path);
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.True(overflow <= 1, $"{path} scrolls horizontally by {overflow}px at 320px width (WCAG 1.4.10).");
        }
    }

    [Fact]
    public async Task Toasts_are_announced_and_dismissible()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/feedback");
        await page.GetByTestId("toast-error").ClickAsync();
        var alert = page.Locator("[data-toasts] [role=alert]");
        await Assertions.Expect(alert).ToContainTextAsync("Upload failed");
        await page.Locator("[data-toast-dismiss]").First.ClickAsync();
        await Assertions.Expect(alert).ToHaveCountAsync(0);
    }
}
