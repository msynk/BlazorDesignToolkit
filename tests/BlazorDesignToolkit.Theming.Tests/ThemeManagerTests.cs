using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Theming.Tests;

public class ThemeManagerTests
{
    [Fact]
    public void Changes_raise_events_with_kinds()
    {
        var manager = new ThemeManager(Samples.Catalog);
        var events = new List<ThemeChangedEventArgs>();
        manager.Changed += (_, e) => events.Add(e);
        manager.SetTheme("ember");
        manager.SetAxis("mode", "dark");
        manager.SetAxis("mode", "dark"); // no-op
        Assert.Equal([ThemeChangeKind.Theme, ThemeChangeKind.Axes], events.Select(e => e.Kind));
        Assert.Equal("aurora", events[0].Previous.ThemeId);
        Assert.Equal("dark", manager.State.EffectiveSelection["mode"]);
    }

    [Fact]
    public void Batches_coalesce_notifications()
    {
        var manager = new ThemeManager(Samples.Catalog);
        var events = new List<ThemeChangedEventArgs>();
        manager.Changed += (_, e) => events.Add(e);
        using (manager.BeginBatch())
        {
            manager.SetTheme("ember");
            manager.SetAxis("density", "compact");
            using (manager.BeginBatch()) manager.SetAxis("contrast", "more");
            Assert.Empty(events);
        }
        var e = Assert.Single(events);
        Assert.Equal(ThemeChangeKind.Theme | ThemeChangeKind.Axes, e.Kind);
        Assert.Equal("aurora", e.Previous.ThemeId);
        Assert.Equal("more", e.Current.Selection["contrast"]);
    }

    [Fact]
    public void Invalid_requests_are_rejected_without_changing_state()
    {
        var manager = new ThemeManager(Samples.Catalog);
        Assert.False(manager.TrySetTheme("nope"));
        Assert.Equal(DiagnosticCatalog.ThemeNotFound.Id, Assert.Single(manager.LastDiagnostics).RuleId);
        Assert.False(manager.TrySetAxis("mode", "sepia"));
        Assert.False(manager.TrySetAxis("density", "system")); // density has no system media queries
        Assert.Throws<ArgumentException>(() => manager.SetAxis("unknown", "x"));
        Assert.Equal(Samples.Catalog.DefaultSelection, manager.State.Selection);
    }

    [Fact]
    public void System_mode_follows_reported_preferences()
    {
        var manager = new ThemeManager(Samples.Catalog);
        manager.SetAxis("mode", "system");
        Assert.Equal("light", manager.State.EffectiveSelection["mode"]);
        var kinds = new List<ThemeChangeKind>();
        manager.Changed += (_, e) => kinds.Add(e.Kind);
        manager.UpdateSystemPreferences(new SystemPreferences { ColorScheme = "dark", Contrast = "no-preference" });
        Assert.Equal("dark", manager.State.EffectiveSelection["mode"]);
        Assert.Equal("system", manager.State.Selection["mode"]);
        Assert.Equal([ThemeChangeKind.SystemPreferences], kinds);
        Assert.Equal("dark", manager.Resolve().Selection["mode"]);
    }

    [Fact]
    public async Task Persists_and_restores_preferences()
    {
        var store = new InMemoryThemePreferenceStore();
        var manager = new ThemeManager(Samples.Catalog, store);
        manager.SetTheme("ember");
        manager.SetAxis("density", "compact");
        Assert.Equal("theme=ember;contrast=standard;density=compact;mode=light", store.Value!.Format());

        var restored = new ThemeManager(Samples.Catalog, store);
        Assert.True(await restored.LoadPreferenceAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ember", restored.State.ThemeId);
        Assert.Equal("compact", restored.State.Selection["density"]);

        await restored.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Null(store.Value);
        Assert.Equal(Samples.Catalog.DefaultSelection, restored.State.Selection);
    }

    [Fact]
    public async Task Unknown_persisted_theme_falls_back_with_diagnostic()
    {
        var store = new InMemoryThemePreferenceStore();
        await store.SaveAsync(new ThemePreference("removed-brand", System.Collections.Immutable.ImmutableSortedDictionary<string, string>.Empty.Add("mode", "dark")), TestContext.Current.CancellationToken);
        var manager = new ThemeManager(Samples.Catalog, store);
        await manager.LoadPreferenceAsync(TestContext.Current.CancellationToken);
        Assert.Equal("aurora", manager.State.ThemeId);
        Assert.Equal("dark", manager.State.Selection["mode"]);
        Assert.Contains(manager.LastDiagnostics, d => d.RuleId == DiagnosticCatalog.ThemeNotFound.Id);
    }

    [Fact]
    public async Task Failing_storage_never_breaks_theming()
    {
        var manager = new ThemeManager(Samples.Catalog, new ThrowingStore());
        Exception? reported = null;
        manager.PersistenceFailed += (_, ex) => reported = ex;
        Assert.False(await manager.LoadPreferenceAsync(TestContext.Current.CancellationToken));
        manager.SetTheme("ember");
        Assert.Equal("ember", manager.State.ThemeId);
        Assert.IsType<InvalidOperationException>(reported);
        await manager.ResetAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Overrides_are_applied_and_cleared()
    {
        var manager = new ThemeManager(Samples.Catalog);
        manager.SetOverride("radius.md", SourceJsonParser.Parse("""{ "value": 2, "unit": "rem" }""").Root!);
        var resolution = manager.Resolve();
        Assert.Equal("""{"value":2,"unit":"rem"}""", resolution.Tokens["component.button.radius"]!.Value!.ToSourceNode().ToJson());
        Assert.Throws<ArgumentException>(() => manager.SetOverride("a..b", new SourceNumber(1)));
        manager.ClearOverrides();
        Assert.True(manager.State.Overrides.IsEmpty);
    }

    [Theory]
    [InlineData("theme=aurora;mode=dark", true)]
    [InlineData("mode=dark", false)]
    [InlineData("theme=aurora;mode=da\"rk", false)]
    [InlineData("theme=<script>", false)]
    [InlineData("", false)]
    public void Preference_text_format_is_strict(string text, bool valid) => Assert.Equal(valid, ThemePreference.TryParse(text, out _));

    private sealed class ThrowingStore : IThemePreferenceStore
    {
        public ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("blocked");
        public ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default) => throw new InvalidOperationException("blocked");
        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("blocked");
    }
}
