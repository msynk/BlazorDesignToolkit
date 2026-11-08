using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>Submenus, menubar, slider, number field, date picker, virtualized lists, dialog and popover fallbacks in a real browser.</summary>
public class NewPrimitivesBrowserTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Submenu_opens_with_right_arrow_and_closes_with_left_arrow(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/menu", mode);
        var trigger = page.GetByTestId("submenu-trigger");
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Copy link" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var sendTo = page.GetByTestId("send-to");
        await Assertions.Expect(sendTo).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(sendTo).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Email" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(sendTo).ToBeFocusedAsync();
        await Assertions.Expect(sendTo).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.GetByTestId("submenu-root")).ToBeVisibleAsync(); // the parent stays open
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Email" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Chat" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter"); // "Chat" closes every menu and returns focus to the menu button
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.GetByText("Last action: Chat")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Menubar_moves_between_menus_and_keeps_one_open()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/menu");
        var bar = page.GetByTestId("menubar");
        var file = bar.GetByRole(AriaRole.Menuitem, new() { Name = "File" });
        var edit = bar.GetByRole(AriaRole.Menuitem, new() { Name = "Edit" });
        await file.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(edit).ToBeFocusedAsync();
        await Assertions.Expect(edit).ToHaveAttributeAsync("tabindex", "0");
        await Assertions.Expect(file).ToHaveAttributeAsync("tabindex", "-1");
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "Undo" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft"); // opens the File menu
        await Assertions.Expect(page.GetByRole(AriaRole.Menuitem, new() { Name = "New" })).ToBeFocusedAsync();
        await Assertions.Expect(edit).ToHaveAttributeAsync("aria-expanded", "false");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(file).ToBeFocusedAsync();
        await Assertions.Expect(file).ToHaveAttributeAsync("aria-expanded", "false");
        await AccessibilityScanTests.AssertNoViolationsAsync(page, "menubar");
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Slider_changes_with_keys_and_pointer(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/inputs", mode);
        var thumb = page.GetByRole(AriaRole.Slider, new() { Name = "Volume" });
        await Assertions.Expect(thumb).ToHaveAttributeAsync("aria-valuenow", "40");
        await thumb.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(thumb).ToHaveAttributeAsync("aria-valuenow", "45");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(thumb).ToHaveAttributeAsync("aria-valuenow", "100");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.scrollY")); // keys do not scroll the page
        // Pointer: click near the left quarter of the track, then drag to the right end.
        var track = page.GetByTestId("slider").Locator("[data-slider-track]");
        var box = (await track.BoundingBoxAsync())!;
        await page.Mouse.ClickAsync(box.X + box.Width * 0.25f, box.Y + box.Height / 2);
        await Assertions.Expect(thumb).ToHaveAttributeAsync("aria-valuenow", "25");
        await Assertions.Expect(thumb).ToBeFocusedAsync();
        await page.Mouse.DownAsync();
        // Drag past the end of the track, but stay inside the viewport (automation reports no usable coordinates outside it).
        await page.Mouse.MoveAsync(Math.Min(box.X + box.Width + 20, page.ViewportSize!.Width - 2), box.Y + box.Height / 2, new() { Steps = 5 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(thumb).ToHaveAttributeAsync("aria-valuenow", "100");
        await Assertions.Expect(page.GetByText("Volume: 100 %")).ToBeVisibleAsync();

        var start = page.GetByRole(AriaRole.Slider, new() { Name = "Minimum price" });
        await start.FocusAsync();
        await page.Keyboard.PressAsync("End"); // limited by the end thumb minus the minimum distance
        await Assertions.Expect(start).ToHaveAttributeAsync("aria-valuenow", "550");
    }

    [Fact]
    public async Task Number_field_steps_and_parses_typed_values()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/inputs");
        var field = page.GetByRole(AriaRole.Spinbutton, new() { Name = "Quantity" });
        await field.FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-valuenow", "2.5");
        await page.Keyboard.PressAsync("PageUp");
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-valuenow", "7.5");
        await field.FillAsync("42");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-valuenow", "20");
        await Assertions.Expect(field).ToHaveValueAsync("20");
        await page.GetByRole(AriaRole.Button, new() { Name = "Decrease" }).ClickAsync();
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-valuenow", "19.5");
        await Assertions.Expect(field).ToBeFocusedAsync(); // the buttons do not take focus
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Date_picker_dialog_follows_the_apg_keyboard_contract(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/inputs?today=2026-11-02", mode); // pins "today" (Monday, 2 November 2026)
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Choose date" });
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("td[data-date='2026-11-02']")).ToBeFocusedAsync(); // Min = today (fixed in the demo)
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("td[data-date='2026-11-03']")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("PageDown");
        await Assertions.Expect(page.Locator("[data-date-picker-heading]")).ToHaveTextAsync("December 2026");
        await Assertions.Expect(page.Locator("td[data-date='2026-12-03']")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(page.GetByText("Delivery: Thursday, December 3, 2026")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Delivery date")).ToHaveValueAsync("12/3/2026");

        // Tab stays inside the open dialog; Escape closes it and returns focus to the button.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("td[data-date='2026-12-03']")).ToBeFocusedAsync();
        for (var i = 0; i < 6; i++) await page.Keyboard.PressAsync("Tab");
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.contains(document.activeElement)"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Virtualized_listbox_renders_a_window_and_reaches_the_last_option()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/listbox");
        var listbox = page.GetByTestId("virtual-listbox");
        await listbox.Locator("[role=option]").First.WaitForAsync(); // Virtualize renders after measuring the viewport
        var rendered = await listbox.Locator("[role=option]").CountAsync();
        Assert.InRange(rendered, 1, 100);
        await listbox.FocusAsync();
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(page.GetByText("Ticket: Ticket #10000")).ToBeVisibleAsync();
        var active = listbox.Locator("[role=option][data-highlighted]");
        await Assertions.Expect(active).ToHaveAttributeAsync("aria-posinset", "10000");
        await Assertions.Expect(active).ToBeInViewportAsync();
        await Assertions.Expect(listbox).ToHaveAttributeAsync("aria-activedescendant", (await active.GetAttributeAsync("id"))!);
        await page.Keyboard.PressAsync("PageUp");
        await Assertions.Expect(page.GetByText("Ticket: Ticket #09990")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Virtualized_tree_moves_focus_to_rows_outside_the_rendered_window()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tree");
        var tree = page.GetByTestId("virtual-tree");
        await tree.FocusAsync();
        await page.Keyboard.PressAsync("End");
        var last = tree.GetByRole(AriaRole.Treeitem, new() { Name = "Folder 100", Exact = true });
        await Assertions.Expect(last).ToHaveAttributeAsync("data-highlighted", "");
        await Assertions.Expect(tree).ToHaveAttributeAsync("aria-activedescendant", (await last.GetAttributeAsync("id"))!);
        await page.Keyboard.PressAsync("ArrowRight"); // expands the last folder
        await Assertions.Expect(last).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("End");
        var file = tree.GetByRole(AriaRole.Treeitem, new() { Name = "Folder 100 / file 19", Exact = true });
        await Assertions.Expect(file).ToHaveAttributeAsync("data-highlighted", "");
        await Assertions.Expect(file).ToBeInViewportAsync();
        await Assertions.Expect(tree).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByText("File: Folder 100 / file 19")).ToBeVisibleAsync();
        Assert.InRange(await tree.GetByRole(AriaRole.Treeitem).CountAsync(), 1, 100);
    }

    [Fact]
    public async Task Dialog_that_ignores_escape_reopens_when_the_browser_closes_it()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/dialog");
        await page.GetByTestId("editor-open").ClickAsync();
        var dialog = page.GetByTestId("editor-dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        // A close watcher (or anything else) closing the native dialog behind the component's back must not win.
        await dialog.EvaluateAsync("d => d.close()");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog).ToHaveJSPropertyAsync("open", true);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Checkbox_mixed_state_and_radio_group_keyboard(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/controls", mode);
        var all = page.GetByRole(AriaRole.Checkbox, new() { Name = "All toppings" });
        await Assertions.Expect(all).ToHaveJSPropertyAsync("indeterminate", true); // one of three toppings is selected
        await all.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(all).ToBeCheckedAsync();
        await Assertions.Expect(all).ToHaveJSPropertyAsync("indeterminate", false);
        await Assertions.Expect(page.GetByRole(AriaRole.Checkbox, new() { Name = "Basil" })).ToBeCheckedAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Olives" }).FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(all).ToHaveJSPropertyAsync("indeterminate", true);
        await Assertions.Expect(all).Not.ToBeCheckedAsync();

        var free = page.GetByRole(AriaRole.Radio, new() { Name = "Free" });
        var pro = page.GetByRole(AriaRole.Radio, new() { Name = "Pro" });
        await free.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(pro).ToBeFocusedAsync();
        await Assertions.Expect(pro).ToBeCheckedAsync();
        await Assertions.Expect(page.GetByText("Selected plan: pro")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(free).ToBeFocusedAsync();
        await Assertions.Expect(page.GetByText("Selected plan: free")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Radiogroup, new() { Name = "Plan" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Form_field_wires_label_description_and_error()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/forms");
        await page.GetByText("Username", new() { Exact = true }).ClickAsync();
        var username = page.GetByTestId("username");
        await Assertions.Expect(username).ToBeFocusedAsync(); // label[for]
        await Assertions.Expect(username).ToHaveAccessibleNameAsync("Username");
        await Assertions.Expect(username).ToHaveAccessibleDescriptionAsync("3 to 20 letters or digits.");
        await Assertions.Expect(username).ToHaveAttributeAsync("required", ""); // native required state
        await username.FillAsync("a!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Assertions.Expect(username).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(username).ToHaveAccessibleDescriptionAsync(new System.Text.RegularExpressions.Regex("Use 3 to 20 letters or digits"));
        await Assertions.Expect(username).ToHaveAccessibleErrorMessageAsync("Use 3 to 20 letters or digits.");
        await username.FillAsync("ada");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Assertions.Expect(username).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.GetByText("Profile saved.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Overlays_work_without_the_popover_api()
    {
        var page = await server.NewPageAsync();
        // Simulate an engine without the Popover API (pre-2024 browsers) before any script runs.
        await page.AddInitScriptAsync("""
            for (const name of ['popover', 'showPopover', 'hidePopover', 'togglePopover']) delete HTMLElement.prototype[name];
            """);
        await page.OpenAsync("/primitives/listbox");
        Assert.False(await page.EvaluateAsync<bool>("() => 'popover' in HTMLElement.prototype"));
        var select = page.GetByTestId("select");
        await select.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        var popup = page.Locator("#" + await select.GetAttributeAsync("aria-controls"));
        await Assertions.Expect(popup).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(popup).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Country: Brazil")).ToBeVisibleAsync();
        // Emulated light dismiss: a click outside closes the popup.
        await select.ClickAsync();
        await Assertions.Expect(popup).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(5, 5);
        await Assertions.Expect(popup).Not.ToBeVisibleAsync();
        await Assertions.Expect(select).ToHaveAttributeAsync("aria-expanded", "false");
    }
}
