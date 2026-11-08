using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>Behavior of the demo application itself: routing, render-mode switching, responsive navigation and pages that
/// depend on the current theme.</summary>
public class DemoAppTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    private static async Task ChooseAsync(IPage page, string testId, string option)
    {
        await page.GetByTestId(testId).ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = option, Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task Unknown_address_renders_the_not_found_page_with_status_404()
    {
        var page = await server.NewPageAsync();
        var response = await page.GotoAsync("/no-such-page");
        Assert.Equal(404, response!.Status);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Page not found", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("^Not found"));
        // The page offers a way on: every section of the demo is linked.
        await Assertions.Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "All demo pages" }).GetByRole(AriaRole.Link, new() { Name = "Token explorer" })).ToBeVisibleAsync();
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Client_side_navigation_to_an_unknown_address_renders_the_not_found_page(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/", mode);
        await page.EvaluateAsync("() => Blazor.navigateTo('/no-such-page')");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Page not found", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("^Not found"));
    }

    [Theory]
    [InlineData("server", "WebAssembly")]
    [InlineData("wasm", "Server")]
    public async Task Render_mode_link_reloads_the_current_page_in_the_other_mode(string mode, string expected)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tabs", mode);
        await page.Locator(".site-footer a").First.ClickAsync();
        await page.Locator(".site-footer strong").GetByText(expected, new() { Exact = true }).WaitForAsync(new() { Timeout = 60000 });
        Assert.Equal("/primitives/tabs", new Uri(page.Url).AbsolutePath); // stays on the same page, not the home page
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tabs", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Narrow_layout_collapses_navigation_behind_a_menu_button()
    {
        var page = await server.NewPageAsync(width: 390, height: 844);
        await page.OpenAsync("/");
        var toggle = page.GetByTestId("nav-toggle");
        var nav = page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(nav).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("theme-select")).ToBeHiddenAsync();

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(nav).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("theme-select")).ToBeVisibleAsync();

        // Following a link closes the menu again.
        await nav.GetByRole(AriaRole.Link, new() { Name = "Tabs" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tabs", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(nav).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(390)]
    [InlineData(320)]
    public async Task Narrow_header_keeps_brand_and_buttons_on_one_row(int width)
    {
        var page = await server.NewPageAsync(width: width, height: 800);
        await page.OpenAsync("/");
        var brand = await page.Locator(".brand").BoundingBoxAsync();
        var toggle = page.GetByTestId("nav-toggle");
        var commands = page.GetByRole(AriaRole.Button, new() { Name = "Commands", Exact = true }); // the label stays in the name when hidden
        foreach (var button in new[] { toggle, commands })
        {
            var box = await button.BoundingBoxAsync();
            Assert.True(box is not null && brand is not null, "Header element not rendered.");
            Assert.True(Math.Abs(box.Y + box.Height / 2 - (brand.Y + brand.Height / 2)) < 8, $"At {width}px a header button wrapped onto another row.");
            Assert.True(box.Width >= 24 && box.Height >= 24, "Target smaller than 24 CSS px (WCAG 2.5.8)."); // SC 2.5.8 Target Size
        }
        await Assertions.Expect(toggle).ToHaveAccessibleNameAsync("Menu");
    }

    [Fact]
    public async Task Wide_layout_shows_navigation_without_a_menu_button()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        await Assertions.Expect(page.GetByTestId("nav-toggle")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Main" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Tree_view_detail_follows_theme_changes()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tree");
        var tree = page.GetByTestId("tree");
        await tree.GetByRole(AriaRole.Treeitem, new() { Name = "color", Exact = true }).ClickAsync();
        await tree.GetByRole(AriaRole.Treeitem, new() { Name = "surface", Exact = true }).ClickAsync();
        await tree.GetByRole(AriaRole.Treeitem, new() { Name = "default", Exact = true }).First.ClickAsync();
        var detail = page.Locator(".tree-detail");
        await Assertions.Expect(detail).ToContainTextAsync("color.surface.default");
        var light = await detail.InnerTextAsync();

        await ChooseAsync(page, "axis-mode", "Dark");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", "dark");
        await Assertions.Expect(detail).Not.ToHaveTextAsync(light);
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Import_accepts_documents_larger_than_the_default_signalr_message_size(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/import-export", mode);
        // About 50 KB of valid tokens: more than SignalR's default 32 KB message limit in Interactive Server mode.
        var groups = string.Join(",", Enumerable.Range(0, 400).Select(i =>
            $"\"g{i}\": {{ \"$type\": \"dimension\", \"a\": {{ \"$value\": {{ \"value\": {i}, \"unit\": \"px\" }} }}, \"b\": {{ \"$value\": {{ \"value\": {i + 1}, \"unit\": \"rem\" }} }} }}"));
        var document = "{ \"space\": { " + groups + " } }";
        Assert.True(document.Length > 40 * 1024);
        await page.GetByTestId("token-json").FillAsync(document);
        await page.GetByTestId("process").ClickAsync();
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "tokens," })).ToContainTextAsync("input.tokens.json: 800 tokens");
        // The circuit is still connected: the page keeps responding.
        await page.GetByRole(AriaRole.Button, new() { Name = "Restore the example" }).ClickAsync();
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "tokens," })).ToContainTextAsync("input.tokens.json: 7 tokens");
    }

    [Fact]
    public async Task Compare_page_lists_differences_between_two_selections()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/compare");
        // Light versus dark Aurora: semantic colors differ, palette tokens do not.
        var status = page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "differ" });
        await Assertions.Expect(status).ToContainTextAsync("value changed");
        var table = page.GetByRole(AriaRole.Table, new() { Name = "Changed tokens" });
        await Assertions.Expect(table.GetByRole(AriaRole.Rowheader, new() { Name = "color.surface.default", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(table.GetByRole(AriaRole.Rowheader, new() { Name = "color.blue.600", Exact = true })).ToHaveCountAsync(0);

        // Identical selections have no differences.
        await ChooseAsync(page, "after-mode", "Light");
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "No differences" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Command_menu_switches_theme_axes_both_ways()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        var input = page.GetByRole(AriaRole.Combobox, new() { Name = "Command menu" });
        async Task RunAsync(string text)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Commands" }).ClickAsync();
            await Assertions.Expect(input).ToBeFocusedAsync();
            await input.FillAsync(text);
            await page.Keyboard.PressAsync("Enter");
        }

        await RunAsync("Density: Compact");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-density", "compact");
        await RunAsync("Density: Comfortable");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-density", "comfortable");
        await RunAsync("Brand: Ember");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "ember");
    }

    [Fact]
    public async Task Editor_override_can_be_removed_individually_and_updates_the_preview()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/editor");
        var combo = page.GetByRole(AriaRole.Combobox, new() { Name = "Token" });
        await combo.FillAsync("color.surface.accent");
        await page.Keyboard.PressAsync("Enter");
        var input = page.Locator("input[type=color]");
        await input.EvaluateAsync("(el) => { el.value = '#ffff00'; el.dispatchEvent(new Event('change', { bubbles: true })); }");
        // Yellow behind white text fails contrast; the editor reports it and the preview shows the new color.
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "checks" })).ToContainTextAsync("fail");
        await Assertions.Expect(page.Locator(".preview-callout--accent")).ToHaveCSSAsync("background-color", "rgb(255, 255, 0)");

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove override for color.surface.accent" }).ClickAsync();
        await Assertions.Expect(page.Locator("html")).Not.ToHaveAttributeAsync("style", new System.Text.RegularExpressions.Regex("--ds-color-surface-accent"));
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "checks" })).ToContainTextAsync("pass");
    }
}
