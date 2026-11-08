using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>
/// Automated stand-ins for parts of the manual assistive-technology review: the accessibility tree that screen readers
/// receive (roles, names, states, structure), Windows High Contrast (forced colors), text spacing (WCAG 1.4.12) and 200%
/// text (WCAG 1.4.4). They do not replace testing with real screen readers; see docs/accessibility.md.
/// </summary>
public class AssistiveTechnologyTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    [Fact]
    public async Task Accessibility_tree_of_menus_menubar_and_inputs()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/menu");
        await Assertions.Expect(page.GetByTestId("menubar")).ToMatchAriaSnapshotAsync("""
            - menubar "Editor":
              - menuitem "File"
              - menuitem "Edit"
              - menuitem "View"
            """);
        await page.GetByTestId("submenu-trigger").ClickAsync();
        await page.GetByTestId("send-to").HoverAsync();
        await Assertions.Expect(page.GetByTestId("send-to")).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.GetByTestId("submenu-root")).ToMatchAriaSnapshotAsync("""
            - menu "Share":
              - menuitem "Copy link"
              - menuitem "Send to" [expanded]
              - menu "Send to":
                - menuitem "Email"
                - menuitem "Chat"
              - menuitem "Embed"
            """);

        await page.OpenAsync("/primitives/inputs");
        await Assertions.Expect(page.GetByTestId("range")).ToMatchAriaSnapshotAsync("""
            - group "Price range":
              - slider "Minimum price"
              - slider "Maximum price"
            """);
        await Assertions.Expect(page.GetByRole(AriaRole.Slider, new() { Name = "Volume" })).ToHaveAttributeAsync("aria-valuetext", "40 percent");
        await page.OpenAsync("/primitives/listbox");
        var listbox = page.GetByTestId("listbox"); // "C#" is selected initially and shows a check mark
        await Assertions.Expect(listbox.GetByRole(AriaRole.Option, new() { Selected = true }).First).ToHaveAccessibleNameAsync("C#"); // the ✓ glyph is not part of the name
        await page.OpenAsync("/primitives/inputs");
        await Assertions.Expect(page.GetByRole(AriaRole.Spinbutton, new() { Name = "Quantity" })).ToHaveAccessibleDescriptionAsync("Between 1 and 20, in steps of 0.5.");
    }

    [Fact]
    public async Task Accessibility_tree_of_the_date_picker_dialog()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/inputs?today=2026-11-02"); // pins "today", so the calendar opens on a known month
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Choose date" });
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-haspopup", "dialog");
        await button.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "November 2026" });
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog).ToMatchAriaSnapshotAsync("""
            - dialog "November 2026":
              - button "Previous year"
              - button "Previous month"
              - heading "November 2026" [level=2]
              - button "Next month"
              - button "Next year"
              - grid "November 2026":
                - rowgroup:
                  - row:
                    - columnheader "Sunday"
                    - columnheader "Monday"
                    - columnheader "Tuesday"
                    - columnheader "Wednesday"
                    - columnheader "Thursday"
                    - columnheader "Friday"
                    - columnheader "Saturday"
            """);
        await Assertions.Expect(page.GetByRole(AriaRole.Gridcell, new() { Name = "Monday, November 2, 2026" })).ToHaveAttributeAsync("aria-current", "date");
        await Assertions.Expect(page.GetByRole(AriaRole.Gridcell, new() { Name = "Sunday, November 1, 2026" })).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(page.GetByLabel("Delivery date")).ToHaveAccessibleDescriptionAsync("M/d/yyyy");
    }

    [Fact]
    public async Task Forced_colors_keep_focus_selection_and_controls_visible()
    {
        if (server.BrowserName != "chromium") Assert.Skip("Forced-colors emulation is available in Chromium only.");
        var context = await server.NewContextAsync(new BrowserNewContextOptions { BaseURL = server.BaseUrl, ForcedColors = ForcedColors.Active });
        var page = await context.NewPageAsync();
        await page.OpenAsync("/primitives/listbox");
        var listbox = page.GetByTestId("listbox");
        await listbox.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Space");
        var highlight = await page.EvaluateAsync<string>("() => { const p = document.createElement('div'); p.style.background = 'Highlight'; document.body.append(p); const c = getComputedStyle(p).backgroundColor; p.remove(); return c; }");
        var selected = listbox.Locator("[role=option][aria-selected=true]").First;
        Assert.Equal(highlight, await selected.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        var outline = await listbox.EvaluateAsync<string>("e => { const s = getComputedStyle(e); return s.outlineStyle + ' ' + s.outlineWidth; }");
        Assert.DoesNotContain("none", outline);
        Assert.NotEqual("0px", outline.Split(' ')[1]);

        await page.OpenAsync("/primitives/inputs");
        var thumb = page.GetByRole(AriaRole.Slider, new() { Name = "Volume" });
        Assert.NotEqual("rgba(0, 0, 0, 0)", await thumb.EvaluateAsync<string>("e => getComputedStyle(e).borderTopColor"));
        Assert.NotEqual("0px", await thumb.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
    }

    public static readonly TheoryData<string> Pages = new("/", "/primitives/controls", "/primitives/forms", "/primitives/menu", "/primitives/inputs", "/primitives/listbox", "/themes");

    /// <summary>WCAG 2.2 SC 1.4.12 Text Spacing: no loss of content with the spacing a user style sheet may apply.</summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Text_spacing_does_not_clip_content(string path)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync(path);
        await page.AddStyleTagAsync(new() { Content = "* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-block-end: 2em !important; }" });
        await AssertNoClippingAsync(page, $"{path} with text spacing");
    }

    /// <summary>WCAG 2.2 SC 1.4.4 Resize Text: 200% text without loss of content or horizontal scrolling at 1280 CSS px.</summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Text_at_200_percent_does_not_clip_or_scroll(string path)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync(path);
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await AssertNoClippingAsync(page, $"{path} at 200% text");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1"), $"{path} scrolls horizontally at 200% text");
    }

    private static async Task AssertNoClippingAsync(IPage page, string context)
    {
        // Interactive controls and labels whose text overflows a box that hides overflow lose content.
        var clipped = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('main button, main a, main label, main [role=option], main [role=tab], main [role=menuitem], main [role=treeitem], main input, main h1, main h2, main p')]
              .filter(e => e.offsetParent !== null)
              .filter(e => {
                const s = getComputedStyle(e);
                const hides = ['hidden', 'clip'].includes(s.overflowX) || ['hidden', 'clip'].includes(s.overflowY) || s.textOverflow === 'ellipsis';
                return hides && (e.scrollWidth > e.clientWidth + 1 || e.scrollHeight > e.clientHeight + 1);
              })
              .map(e => e.tagName.toLowerCase() + ': ' + (e.textContent || e.value || '').trim().slice(0, 40))
            """);
        Assert.True(clipped.Length == 0, $"{context}: clipped content\n{string.Join("\n", clipped)}");
    }
}
