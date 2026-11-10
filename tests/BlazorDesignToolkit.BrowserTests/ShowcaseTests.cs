using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BlazorDesignToolkit.BrowserTests;

/// <summary>The demo's showcase pages: the introduction, the guided tour, the sample app, the brand builder, the primitive
/// skins and the live markup inspector.</summary>
public class ShowcaseTests(DemoServer server) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await server.CloseContextsAsync();

    [Fact]
    public async Task Home_switcher_changes_the_site_theme_and_highlights_the_changed_attribute()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/");
        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Try it: restyle this entire site" });

        await panel.GetByRole(AriaRole.Radio, new() { Name = "Dark", Exact = true }).CheckAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-mode", "dark");
        await Assertions.Expect(panel.Locator(".code-line mark")).ToHaveTextAsync("data-mode=\"dark\"");

        await panel.GetByRole(AriaRole.Radio, new() { Name = "Ember", Exact = true }).CheckAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "ember");
        await Assertions.Expect(panel.Locator(".code-line mark")).ToHaveTextAsync("data-theme=\"ember\"");
    }

    [Fact]
    public async Task Tour_traces_references_and_checks_every_theme()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/tour");
        // component.button.primary.background → color.surface.accent → color.blue.600 in Aurora light.
        var chain = page.GetByTestId("trace-chain");
        await Assertions.Expect(chain).ToContainTextAsync("color.surface.accent");
        await Assertions.Expect(chain).ToContainTextAsync("color.blue.600");

        await page.GetByTestId("check-all-themes").ClickAsync();
        await Assertions.Expect(page.GetByTestId("all-themes-result")).ToContainTextAsync("Checked 16 themes");
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Showcase_creates_a_task_through_a_validated_dialog(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/showcase?today=2026-03-02", mode);
        await page.GetByTestId("new-task").ClickAsync();
        var dialog = page.GetByTestId("new-task-dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        await page.GetByTestId("create-task").ClickAsync();
        await Assertions.Expect(page.GetByTestId("task-title")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(dialog.GetByText("Enter a title for the task.")).ToBeVisibleAsync();

        await page.GetByTestId("task-title").FillAsync("Ship the demo");
        await page.GetByTestId("create-task").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("task-table").GetByRole(AriaRole.Rowheader, new() { Name = "Ship the demo" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Showcase_preview_settings_theme_only_the_app()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/showcase");
        await page.GetByTestId("showcase-brand").GetByRole(AriaRole.Radio, new() { Name = "Ember" }).CheckAsync();
        await page.GetByTestId("showcase-mode").GetByRole(AriaRole.Radio, new() { Name = "Dark" }).CheckAsync();

        var app = page.GetByTestId("showcase-app");
        await Assertions.Expect(app).ToHaveAttributeAsync("data-theme", "ember");
        await Assertions.Expect(app).ToHaveAttributeAsync("data-mode", "dark");
        await Assertions.Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-theme", "ember");
        await Assertions.Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-mode", "dark");

        // The search finds tasks in other projects.
        await page.GetByTestId("task-search").FillAsync("Offline");
        await page.GetByRole(AriaRole.Option, new() { Name = "Offline sync" }).WaitForAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(app.GetByRole(AriaRole.Heading, new() { Name = "Mobile app", Level = 2 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Showcase_dialog_has_no_violations()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/showcase");
        await page.GetByTestId("new-task").ClickAsync();
        await page.GetByTestId("new-task-dialog").WaitForAsync();
        await AccessibilityScanTests.AssertNoViolationsAsync(page, "showcase new task dialog");
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Brand_builder_applies_the_palette_to_the_site_and_removes_it(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/brand-builder", mode);
        await page.GetByTestId("brand-preset").GetByRole(AriaRole.Radio, new() { Name = "Violet" }).CheckAsync();
        await Assertions.Expect(page.GetByTestId("follower-count")).Not.ToHaveTextAsync("0 other tokens");

        await page.GetByTestId("apply-brand").ClickAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("style", new Regex("--ds-color-blue-600"));

        await page.GetByTestId("remove-brand").ClickAsync();
        await Assertions.Expect(page.Locator("html")).Not.ToHaveAttributeAsync("style", new Regex("--ds-color-blue-600"));
    }

    [Fact]
    public async Task Primitive_skins_change_the_styles_but_not_the_behavior()
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives");
        var stage = page.GetByTestId("skin-stage");
        var first = stage.GetByRole(AriaRole.Tab).Nth(0);

        await page.GetByTestId("skin").GetByRole(AriaRole.Radio, new() { Name = "Custom CSS" }).CheckAsync();
        await Assertions.Expect(stage).ToHaveClassAsync(new Regex("skin--blueprint"));
        Assert.Equal("uppercase", await first.EvaluateAsync<string>("e => getComputedStyle(e).textTransform"));

        await page.GetByTestId("skin").GetByRole(AriaRole.Radio, new() { Name = "No CSS at all" }).CheckAsync();
        await Assertions.Expect(stage).ToHaveClassAsync(new Regex("skin--none"));
        Assert.Equal("none", await first.EvaluateAsync<string>("e => getComputedStyle(e).textTransform"));

        // Without any CSS the tabs still work with the keyboard.
        await first.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(stage.GetByRole(AriaRole.Tab).Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(stage.GetByRole(AriaRole.Tab).Nth(1)).ToBeFocusedAsync();
    }

    [Theory]
    [MemberData(nameof(KeyboardTests.Modes), MemberType = typeof(KeyboardTests))]
    public async Task Live_markup_follows_aria_state_changes(string mode)
    {
        var page = await server.NewPageAsync();
        await page.OpenAsync("/primitives/tabs", mode);
        var example = page.GetByRole(AriaRole.Region, new() { Name = "Automatic activation" });
        var trigger = example.GetByRole(AriaRole.Button, new() { Name = "Live markup" });
        var output = example.Locator(".live-markup__output");

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(output).ToContainTextAsync(new Regex("role=\"tab\"[^<]*aria-selected=\"true\"[^<]*>Overview<"));

        await example.GetByRole(AriaRole.Tab, new() { Name = "Activity" }).ClickAsync();
        await Assertions.Expect(output).ToContainTextAsync(new Regex("aria-selected=\"true\"[^<]*>Activity<"));
        await Assertions.Expect(output).ToContainTextAsync(new Regex("aria-selected=\"false\"[^<]*>Overview<"));

        await trigger.ClickAsync();
        await Assertions.Expect(output).ToHaveTextAsync("");
    }
}
