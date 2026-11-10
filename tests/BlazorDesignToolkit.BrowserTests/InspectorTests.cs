using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>The demo's site-wide tools: the inspector, the visual style guide, the page navigation and the header layout.</summary>
public class InspectorTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    private static async Task ChooseAsync(IPage page, string testId, string option)
    {
        await page.GetByTestId(testId).ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = option, Exact = true }).ClickAsync();
    }

    /// <summary>Opens the inspector, which starts picking, and stops picking so the page can be used.</summary>
    internal static async Task OpenInspectorAsync(IPage page)
    {
        await page.GetByTestId("inspect-toggle").ClickAsync();
        var pick = page.GetByTestId("inspector-pick");
        await Assertions.Expect(pick).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(pick).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Inspector_traces_the_tokens_of_a_picked_element_to_the_current_theme(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/", mode);
        await page.GetByTestId("inspect-toggle").ClickAsync();
        var pick = page.GetByTestId("inspector-pick");
        await Assertions.Expect(pick).ToHaveAttributeAsync("aria-pressed", "true");

        // While picking, a click selects the element instead of following the link.
        await page.GetByRole(AriaRole.Link, new() { Name = "Take the guided tour" }).ClickAsync();
        await Assertions.Expect(pick).ToHaveAttributeAsync("aria-pressed", "false");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.GetByTestId("inspector-target")).ToContainTextAsync("button--primary");
        await Assertions.Expect(page.GetByTestId("inspector-speech")).ToHaveTextAsync("“Take the guided tour”, link");

        await page.GetByTestId("inspector").GetByRole(AriaRole.Tab, new() { Name = "Tokens" }).ClickAsync();
        var tokens = page.GetByTestId("inspector-tokens");
        // component → semantic → primitive → value, as resolved for Aurora light.
        await Assertions.Expect(tokens).ToContainTextAsync("component.button.primary.background");
        await Assertions.Expect(tokens).ToContainTextAsync("color.surface.accent");
        await Assertions.Expect(tokens).ToContainTextAsync("color.blue.600");

        // In Ember the same component token ends at another primitive.
        await ChooseAsync(page, "theme-select", "Ember");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "ember");
        await pick.ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Take the guided tour" }).ClickAsync();
        await Assertions.Expect(tokens).ToContainTextAsync("color.ember.");
        await Assertions.Expect(tokens).Not.ToContainTextAsync("color.blue.600");
    }

    [Fact]
    public async Task Inspector_follows_the_focus_and_describes_what_a_screen_reader_announces()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/showcase");
        await OpenInspectorAsync(page);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).FocusAsync();
        var speech = page.GetByTestId("inspector-speech");
        await Assertions.Expect(speech).ToHaveTextAsync("“Tasks 4”, tab, selected, 2 of 3");
        await Assertions.Expect(page.GetByTestId("inspector-accessibility")).ToContainTextAsync("tab list “Project views”");

        // Roving focus: the arrow key moves focus and selection, and the description follows.
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(speech).ToHaveTextAsync("“Settings”, tab, selected, 3 of 3");
        await Assertions.Expect(page.GetByTestId("inspector-accessibility")).ToContainTextAsync("tabindex=\"0\"");
    }

    [Fact]
    public async Task Inspector_logs_live_region_announcements()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/feedback");
        await OpenInspectorAsync(page);
        await page.GetByTestId("inspector").GetByRole(AriaRole.Tab, new() { Name = "Announcements" }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Show success" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("inspector-announcements")).ToContainTextAsync("Settings saved");
    }

    [Fact]
    public async Task Inspector_closes_from_its_panel_and_from_the_header()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        var toggle = page.GetByTestId("inspect-toggle");
        await OpenInspectorAsync(page);
        await page.GetByTestId("inspector-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("inspector")).ToHaveCountAsync(0);
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");

        // The introduction page opens it too, and following a link keeps it open.
        await page.GetByTestId("open-inspector").ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Keyboard.PressAsync("Escape");
        await page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Visual style guide" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Visual style guide", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("inspector")).ToBeVisibleAsync();
        await toggle.ClickAsync();
        await Assertions.Expect(page.GetByTestId("inspector")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Header_brand_and_buttons_line_up_with_the_theme_selects()
    {
        var page = await server.NewPageAsync(width: 1440, height: 900);
        await page.OpenAsync("/");
        await AssertAlignedAsync(page, "inspector closed");

        // Docked, the inspector narrows the page; the header stays on one row.
        await OpenInspectorAsync(page);
        await AssertAlignedAsync(page, "inspector docked");

        static async Task AssertAlignedAsync(IPage page, string context)
        {
            var select = await page.GetByTestId("theme-select").BoundingBoxAsync();
            Assert.NotNull(select);
            var others = new[] { page.Locator(".brand"), page.GetByTestId("inspect-toggle"), page.GetByRole(AriaRole.Button, new() { Name = "Commands", Exact = true }) };
            foreach (var other in others)
            {
                var box = await other.BoundingBoxAsync();
                Assert.NotNull(box);
                var offset = box.Y + box.Height / 2 - (select.Y + select.Height / 2);
                Assert.True(Math.Abs(offset) < 2, $"{context}: {other} is {offset:0.#}px off the middle of the theme selects.");
            }
        }
    }

    [Fact]
    public async Task Pages_link_to_the_previous_and_the_next_page()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        var pager = page.GetByRole(AriaRole.Navigation, new() { Name = "Previous and next page" });
        await Assertions.Expect(pager.GetByRole(AriaRole.Link)).ToHaveCountAsync(1);
        await Assertions.Expect(pager.GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", "tour");

        await page.OpenAsync("/style-guide");
        // The visually hidden colon separates direction and title for screen readers.
        await Assertions.Expect(pager.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^Previous\W+Token explorer") })).ToBeVisibleAsync();
        await pager.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^Next\W+Validation") }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/validation$"));
        await Assertions.Expect(pager.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^Next\W+Compare themes") })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Style_guide_is_generated_from_and_follows_the_current_theme()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/style-guide");
        await Assertions.Expect(page.Locator("#scales").GetByRole(AriaRole.Heading, new() { Name = "color.blue", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#contrast")).ToContainTextAsync(new Regex(@"(\d+) of \1 pass"));

        // Ember adds its own scale; the semantic accent points at it.
        await ChooseAsync(page, "theme-select", "Ember");
        await Assertions.Expect(page.Locator("#scales").GetByRole(AriaRole.Heading, new() { Name = "color.ember", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#semantic")).ToContainTextAsync("color.ember.");

        // Density changes the size tokens, and with them the rendered controls.
        var control = page.Locator(".density-demo__control").First;
        var comfortable = (await control.BoundingBoxAsync())!.Height;
        await ChooseAsync(page, "axis-density", "Compact");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-density", "compact");
        Assert.True((await control.BoundingBoxAsync())!.Height < comfortable, "Compact density did not shrink the control.");

        await page.GetByTestId("play-motion").ClickAsync();
        await Assertions.Expect(page.Locator(".motion")).ToHaveAttributeAsync("data-run", "");
    }
}
