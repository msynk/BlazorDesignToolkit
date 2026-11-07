using System.Text.Json;
using BlazorDesignToolkit.Theming.Blazor;
using BlazorDesignToolkit.Tokens.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDesignToolkit.Theming.Tests;

public class BlazorAdapterTests : BunitContext
{
    private BunitJSModuleInterop _module = null!;

    private void Configure(ThemePersistence persistence = ThemePersistence.None)
    {
        Services.AddDesignTokenTheming(o =>
        {
            o.Catalog = Samples.Catalog;
            o.Persistence = persistence;
        });
        _module = JSInterop.SetupModule(ThemeBrowserInterop.ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
        _module.Setup<bool>("apply", _ => true).SetResult(true);
        _module.Setup<JsonElement?>("getSystemPreferences").SetResult(JsonDocument.Parse("""{ "colorScheme": "dark", "contrast": "no-preference", "forcedColors": false, "reducedMotion": true }""").RootElement);
        _module.Setup<int>("watchSystemPreferences", _ => true).SetResult(7);
    }

    [Fact]
    public void ThemeStyles_renders_the_generated_stylesheet()
    {
        Configure();
        var cut = Render<ThemeStyles>();
        var style = cut.Find("style#bdt-theme-styles");
        Assert.Contains("[data-theme=\"ember\"]", style.TextContent);
        Assert.Contains("@media (prefers-color-scheme: dark)", style.TextContent);
    }

    [Fact]
    public void Init_script_is_only_rendered_with_persistence()
    {
        Configure();
        Assert.Empty(Render<ThemeInitScript>().FindAll("script"));
    }

    [Fact]
    public void Init_script_carries_validated_configuration()
    {
        Configure(ThemePersistence.LocalStorage);
        var config = JsonDocument.Parse(Render<ThemeInitScript>().Find("script").GetAttribute("data-config")!).RootElement;
        Assert.Equal("localStorage", config.GetProperty("storage").GetString());
        Assert.Equal(["aurora", "ember"], config.GetProperty("themes").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("system", config.GetProperty("axes").GetProperty("mode").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("system", config.GetProperty("axes").GetProperty("density").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void ThemeScope_renders_all_axis_attributes()
    {
        Configure();
        var cut = Render<ThemeScope>(p => p
            .Add(s => s.ThemeId, "ember")
            .Add(s => s.Axes, new Dictionary<string, string> { ["mode"] = "dark" })
            .AddUnmatched("class", "preview")
            .AddChildContent("<p>x</p>"));
        var div = cut.Find("div");
        Assert.Equal("ember", div.GetAttribute("data-theme"));
        Assert.Equal("dark", div.GetAttribute("data-mode"));
        Assert.Equal("standard", div.GetAttribute("data-contrast"));
        Assert.Equal("comfortable", div.GetAttribute("data-density"));
        Assert.Equal("preview", div.GetAttribute("class"));
    }

    [Fact]
    public void ThemeScope_applies_overrides_to_its_subtree_only()
    {
        Configure();
        IReadOnlyList<BlazorDesignToolkit.Tokens.Diagnostics.Diagnostic>? reported = null;
        var red = BlazorDesignToolkit.Tokens.Json.SourceJsonParser.Parse("""{ "colorSpace": "srgb", "components": [1, 0, 0] }""").Root!;
        var cut = Render<ThemeScope>(p => p
            .Add(s => s.ThemeId, "aurora")
            .Add(s => s.Overrides, new Dictionary<string, BlazorDesignToolkit.Tokens.Json.SourceNode> { ["color.blue.600"] = red, ["no.such.token"] = red })
            .Add(s => s.OverrideDiagnostics, d => reported = d)
            .AddUnmatched("style", "outline: 1px solid")
            .AddChildContent("<p>x</p>"));
        var style = cut.Find("div").GetAttribute("style")!;
        Assert.Matches(@"--[a-z-]*color-blue-600: #ff0000;", style);
        Assert.EndsWith("outline: 1px solid", style);  // the author's own style is kept and wins
        Assert.NotNull(reported);
        Assert.Contains(reported!, d => d.Severity == BlazorDesignToolkit.Tokens.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ThemeScope_does_not_recompute_for_equal_new_dictionaries()
    {
        Configure();
        // With reference comparison this re-rendered forever: diagnostics → parent render → new dictionaries → diagnostics.
        var cut = Render<ThemeScopeHost>();
        Assert.Equal(1, cut.Instance.Reports);
        Assert.NotEmpty(cut.Instance.Last!);
        Assert.Matches(@"--[a-z-]*color-blue-600: #ff0000;", cut.Find("div").GetAttribute("style")!);
        cut.Render();
        Assert.Equal(1, cut.Instance.Reports);
        // A real change recomputes and reports again.
        cut.Render(p => p.Add(h => h.Mode, "light"));
        Assert.Equal(2, cut.Instance.Reports);
        Assert.Equal("light", cut.Find("div").GetAttribute("data-mode"));
    }

    [Fact]
    public void ThemeProvider_applies_state_follows_system_and_reacts_to_changes()
    {
        Configure();
        var cut = Render<ThemeProvider>(p => p.AddChildContent("<main>content</main>"));
        cut.WaitForAssertion(() => Assert.Single(_module.Invocations, i => i.Identifier == "apply"));

        var manager = Services.GetRequiredService<ThemeManager>();
        Assert.Equal(new SystemPreferences { ColorScheme = "dark", Contrast = "no-preference", ReducedMotion = true }, manager.State.SystemPreferences);
        var first = _module.Invocations.Single(i => i.Identifier == "apply");
        var attributes = (IReadOnlyDictionary<string, string?>)first.Arguments[1]!;
        Assert.Equal("aurora", attributes["data-theme"]);
        Assert.Equal("light", attributes["data-mode"]);

        cut.InvokeAsync(() => manager.SetAxis("mode", "system"));
        cut.WaitForAssertion(() => Assert.Equal(2, _module.Invocations.Count(i => i.Identifier == "apply")));
        Assert.Equal("system", ((IReadOnlyDictionary<string, string?>)_module.Invocations.Last(i => i.Identifier == "apply").Arguments[1]!)["data-mode"]);
        Assert.Equal("dark", manager.State.EffectiveSelection["mode"]);

        cut.InvokeAsync(() => manager.SetOverride("color.blue.600", SourceJsonParser.Parse("""{ "colorSpace": "srgb", "components": [1, 0, 0] }""").Root!));
        cut.WaitForAssertion(() => Assert.Equal(3, _module.Invocations.Count(i => i.Identifier == "apply")));
        var properties = (IReadOnlyDictionary<string, string?>)_module.Invocations.Last(i => i.Identifier == "apply").Arguments[2]!;
        Assert.Equal("#ff0000", properties["--color-blue-600"]);

        cut.InvokeAsync(manager.ClearOverrides);
        cut.WaitForAssertion(() => Assert.Equal(4, _module.Invocations.Count(i => i.Identifier == "apply")));
        var cleared = (IReadOnlyCollection<string>)_module.Invocations.Last(i => i.Identifier == "apply").Arguments[3]!;
        Assert.Contains("--color-blue-600", cleared);
        Assert.Contains("content", cut.Markup);
    }

    [Fact]
    public async Task ThemeProvider_unwatches_on_dispose()
    {
        Configure();
        var cut = Render<ThemeProvider>();
        cut.WaitForAssertion(() => Assert.Contains(_module.Invocations, i => i.Identifier == "apply"));
        await DisposeComponentsAsync();
        Assert.Contains(_module.Invocations, i => i.Identifier == "unwatch" && (int)i.Arguments[0]! == 7);
    }

    [Fact]
    public void LocalStorage_persistence_loads_preference_on_first_render()
    {
        Configure(ThemePersistence.LocalStorage);
        _module.Setup<string?>("loadStoredPreference", _ => true).SetResult("theme=ember;mode=dark;density=compact");
        Render<ThemeProvider>();
        var manager = Services.GetRequiredService<ThemeManager>();
        Assert.Equal("ember", manager.State.ThemeId);
        Assert.Equal("compact", manager.State.Selection["density"]);
    }

    [Theory]
    [InlineData("theme%3Dember%3Bmode%3Ddark", "ember", "dark")]
    [InlineData("theme=ember;mode=dark", "ember", "dark")]
    public void Cookie_values_are_parsed_for_server_rendering(string cookie, string theme, string mode)
    {
        var selection = ThemeRootAttributes.FromCookie(cookie)!;
        Assert.Equal(theme, selection.ThemeId);
        Assert.Equal(mode, selection["mode"]);
        var attributes = ThemeRootAttributes.For(Samples.Catalog, selection);
        Assert.Equal("standard", attributes["data-contrast"]);
        Assert.Null(ThemeRootAttributes.FromCookie("garbage\"><script>"));
    }
}
