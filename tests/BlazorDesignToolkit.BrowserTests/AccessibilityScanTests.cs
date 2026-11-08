using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>axe-core scans of every demo page and of opened overlay states, in several themes and both render modes.</summary>
public class AccessibilityScanTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    public static readonly TheoryData<string> Pages = new(
        "/", "/tokens", "/themes", "/compare", "/editor", "/import-export", "/validation", "/coverage", "/integrations", "/design-system", "/accessibility", "/not-found",
        "/primitives/controls", "/primitives/forms", "/primitives/tabs", "/primitives/disclosure", "/primitives/dialog", "/primitives/popover",
        "/primitives/listbox", "/primitives/menu", "/primitives/inputs", "/primitives/tree", "/primitives/feedback");

    private static readonly AxeRunOptions s_wcag = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
    };

    internal static async Task AssertNoViolationsAsync(IPage page, string context)
    {
        AxeResult result;
        try
        {
            result = await page.RunAxe(s_wcag);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("removeBrowserContext", StringComparison.Ordinal))
        {
            // RunAxe finishes the run in a temporary browser context. Firefox intermittently fails to remove such contexts
            // ("_maybeDontRestoreTabs" in its session store). The demo pages have no iframes, so the in-page run is equivalent.
#pragma warning disable CS0618 // Legacy mode: used only as the fallback described above.
            result = await page.RunAxeLegacy(options: s_wcag);
#pragma warning restore CS0618
        }
        var violations = result.Violations.Select(v =>
            $"{v.Id} ({v.Impact}): {v.Help}\n" + string.Join("\n", v.Nodes.Take(5).Select(n => $"    {n.Target}: {n.Html}"))).ToList();
        Assert.True(violations.Count == 0, $"{context}: {violations.Count} axe violation(s)\n{string.Join("\n", violations)}");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Pages_have_no_wcag_violations_in_light_mode(string path)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync(path);
        await AssertNoViolationsAsync(page, $"{path} (light)");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Pages_have_no_wcag_violations_in_dark_ember_with_more_contrast(string path)
    {
        var page = await server.NewPageAsync(ColorScheme.Dark, Contrast.More);
        await page.Context.AddCookiesAsync([new Cookie { Name = "bdt-theme", Value = Uri.EscapeDataString("theme=ember;contrast=system;density=compact;mode=system"), Url = server.BaseUrl }]);
        await page.OpenAsync(path);
        Assert.Equal("system", await page.Locator("html").GetAttributeAsync("data-mode"));
        await AssertNoViolationsAsync(page, $"{path} (ember, system dark, more contrast)");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/primitives/tabs")]
    [InlineData("/primitives/listbox")]
    public async Task Pages_have_no_wcag_violations_in_webassembly_mode(string path)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync(path, "wasm");
        await AssertNoViolationsAsync(page, $"{path} (WebAssembly)");
    }

    [Fact]
    public async Task Open_overlays_have_no_violations()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/dialog");
        await page.GetByTestId("open-dialog").ClickAsync();
        await page.GetByTestId("dialog").WaitForAsync();
        await AssertNoViolationsAsync(page, "open dialog");

        await page.OpenAsync("/primitives/menu");
        await page.GetByTestId("menu-trigger").ClickAsync();
        await page.Locator("[role=menuitem]").First.WaitForAsync();
        await AssertNoViolationsAsync(page, "open menu");

        await page.OpenAsync("/primitives/listbox");
        await page.GetByTestId("select").ClickAsync();
        await page.Locator("[role=option]").First.WaitForAsync();
        await AssertNoViolationsAsync(page, "open select");

        await page.GetByTestId("select").PressAsync("Escape");
        await page.GetByTestId("combobox").FillAsync("b");
        await page.Locator("[role=option]").First.WaitForAsync();
        await AssertNoViolationsAsync(page, "open combobox");

        await page.OpenAsync("/primitives/feedback");
        await page.GetByTestId("toast-error").ClickAsync();
        await page.Locator("[data-toasts] li").First.WaitForAsync();
        await AssertNoViolationsAsync(page, "toasts");

        await page.OpenAsync("/primitives/menu");
        await page.GetByTestId("submenu-trigger").ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Copy link" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.GetByTestId("send-to")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.GetByTestId("send-to-menu").Locator("[role=menuitem]").First.WaitForAsync();
        await AssertNoViolationsAsync(page, "open submenu");
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Escape");
        await page.GetByTestId("menubar").GetByRole(AriaRole.Menuitem, new() { Name = "File" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "New" }).WaitForAsync();
        await AssertNoViolationsAsync(page, "open menubar menu");

        await page.OpenAsync("/primitives/inputs");
        await page.GetByRole(AriaRole.Button, new() { Name = "Choose date" }).ClickAsync();
        await page.GetByRole(AriaRole.Grid).WaitForAsync();
        await AssertNoViolationsAsync(page, "open date picker");

        await page.OpenAsync("/");
        var input = page.GetByRole(AriaRole.Combobox, new() { Name = "Command menu" });
        for (var attempt = 0; attempt < 5 && !await input.IsVisibleAsync(); attempt++)
        {
            await page.Keyboard.PressAsync("Control+k");
            try { await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 2000 }); } catch (TimeoutException) { }
        }
        await page.Keyboard.TypeAsync("the");
        await AssertNoViolationsAsync(page, "open command menu");
    }
}
