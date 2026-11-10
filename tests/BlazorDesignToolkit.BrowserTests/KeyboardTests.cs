using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>Real keyboard operation and focus management in a browser, in both interactive render modes.</summary>
public class KeyboardTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    public static readonly TheoryData<string> Modes = new("server", "wasm");

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Tabs_use_roving_focus_and_skip_disabled_tabs(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tabs", mode);
        var tabs = page.GetByTestId("tabs-auto").GetByRole(AriaRole.Tab);
        await tabs.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(tabs.Nth(1)).ToBeFocusedAsync();
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("ArrowRight"); // skips the disabled "Archived" tab
        await Assertions.Expect(tabs.Nth(3)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowRight"); // wraps
        await Assertions.Expect(tabs.First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(tabs.Nth(3)).ToBeFocusedAsync();
        // Focus moves before the selection is rendered; in Server mode, wait for it so that Tab finds the new panel.
        await Assertions.Expect(tabs.Nth(3)).ToHaveAttributeAsync("aria-selected", "true");
        await page.Keyboard.PressAsync("Tab"); // into the active panel
        await Assertions.Expect(page.GetByTestId("tabs-auto").GetByRole(AriaRole.Tabpanel)).ToBeFocusedAsync();
        // Arrow keys did not scroll the page.
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.scrollY"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Modal_dialog_moves_focus_contains_it_and_restores_it(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/dialog", mode);
        var trigger = page.GetByTestId("open-dialog");
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var dialog = page.GetByTestId("dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("dialog-name")).ToBeFocusedAsync();
        for (var i = 0; i < 6; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var inside = await page.EvaluateAsync<bool>("() => { const a = document.activeElement; return !a || a === document.body || !!a.closest('dialog'); }");
            Assert.True(inside, "Focus left the modal dialog.");
        }
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('main').inert || document.querySelector('dialog').matches(':modal')"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Dialog_save_updates_state_and_closes()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/dialog");
        await page.GetByTestId("open-dialog").ClickAsync();
        await page.GetByTestId("dialog-name").FillAsync("Grace");
        await page.GetByTestId("dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("dialog")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(new LocatorFilterOptions { HasText = "Saved name" })).ToHaveTextAsync("Saved name: Grace");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Menu_button_keyboard_contract(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/menu", mode);
        var trigger = page.GetByTestId("menu-trigger");
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var items = page.GetByTestId("menu").Locator("[role^=menuitem]");
        await Assertions.Expect(items.First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(items.Nth(1)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown"); // skips the disabled item
        await Assertions.Expect(items.Nth(3)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(items.First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("d"); // typeahead: Duplicate
        await Assertions.Expect(items.Nth(1)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("menu")).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await Assertions.Expect(page.GetByText("Last action: Duplicate")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("ArrowUp"); // opens on the last item
        await Assertions.Expect(items.Last).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("menu")).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Select_and_combobox_keyboard_selection(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/listbox", mode);
        var select = page.GetByTestId("select");
        await select.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(select).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(select).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(select).ToBeFocusedAsync();
        await Assertions.Expect(page.GetByText("Country: Brazil")).ToBeVisibleAsync();

        var combo = page.GetByTestId("combobox");
        await combo.FocusAsync();
        await page.Keyboard.TypeAsync("to");
        var popup = page.Locator("#" + await combo.GetAttributeAsync("aria-controls"));
        await Assertions.Expect(popup.Locator("[role=option]")).ToHaveTextAsync(["Cape Town", "Tokyo", "Toronto"]);
        await Assertions.Expect(page.GetByText("3 results available.")).ToBeAttachedAsync();
        await page.Keyboard.PressAsync("ArrowDown"); // the first match is highlighted automatically; move to the second
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(combo).ToHaveValueAsync("Tokyo");
        await Assertions.Expect(page.GetByText("City: Tokyo")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Listbox_multiselect_keyboard()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/listbox");
        var listbox = page.GetByTestId("listbox");
        await listbox.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.GetByText("Selected: C#, F#")).ToBeVisibleAsync();
        var active = await listbox.GetAttributeAsync("aria-activedescendant");
        await Assertions.Expect(page.Locator($"#{active}")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task Tooltip_shows_on_focus_and_escape_dismisses_without_moving_focus()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/popover");
        var trigger = page.GetByTestId("tooltip-trigger");
        var tooltip = page.GetByTestId("tooltip");
        await page.Locator("[data-testid=tooltip][data-tooltip-ready]").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        await trigger.FocusAsync();
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tooltip).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await trigger.HoverAsync();
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await tooltip.HoverAsync(); // hoverable: the pointer may move onto the tooltip
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Popover_light_dismiss_restores_focus()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/popover");
        var trigger = page.GetByTestId("popover-trigger");
        await trigger.ClickAsync();
        var popover = page.GetByTestId("popover");
        await Assertions.Expect(popover).ToBeVisibleAsync();
        await Assertions.Expect(popover.GetByRole(AriaRole.Combobox)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Tree_keyboard_navigation()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tree");
        var tree = page.GetByTestId("tree");
        var first = tree.Locator("[role=treeitem]").First;
        await first.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(first).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("ArrowRight");
        var child = first.Locator("[role=group] > [role=treeitem]").First;
        await Assertions.Expect(child).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(first).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(first).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task Accordion_arrow_keys_move_between_headers()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/disclosure");
        var triggers = page.GetByTestId("accordion").Locator("[data-accordion-trigger]");
        await triggers.First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(triggers.Nth(1)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(triggers.Nth(1)).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(triggers.First).ToHaveAttributeAsync("aria-expanded", "false");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(triggers.First).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Switch_and_toggle_respond_to_space()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/controls");
        var toggle = page.GetByTestId("toggle-bold");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        var sw = page.GetByTestId("switch");
        await sw.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(sw).ToHaveAttributeAsync("aria-checked", "false");
    }

    [Fact]
    public async Task Command_menu_opens_with_shortcut_and_navigates()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        var input = page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = "Command menu" });
        // The shortcut is registered right after the first interactive render. The shortcut toggles the menu, so wait long
        // enough after each press that a slow open is never mistaken for a missed press.
        for (var attempt = 0; attempt < 5 && !await input.IsVisibleAsync(); attempt++)
        {
            await page.Keyboard.PressAsync("Control+k");
            try { await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 2000 }); } catch (TimeoutException) { }
        }
        await Assertions.Expect(input).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("tree");
        // Typing quickly while renders are in flight must not lose characters (regression: value + @oninput).
        await Assertions.Expect(input).ToHaveValueAsync("tree");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/primitives/tree"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Tree view", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Skip_link_moves_focus_to_main()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        // The skip link is the first element in the tab order.
        var first = await page.EvaluateAsync<string>("() => [...document.querySelectorAll('a[href],button,input,select,textarea,[tabindex]')].find(e => e.tabIndex >= 0 && !e.disabled)?.textContent?.trim()");
        Assert.Equal("Skip to main content", first);
        var skip = page.GetByText("Skip to main content");
        await skip.FocusAsync();
        await Assertions.Expect(skip).ToBeVisibleAsync(); // visible when focused (WCAG 2.4.7 / 2.4.11)
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
    }
}
