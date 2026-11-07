using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace BlazorDesignToolkit.Theming.Blazor;

public enum ThemePersistence
{
    /// <summary>No persistence (default). Nothing is written to the browser.</summary>
    None,
    /// <summary>localStorage. Read by the early-initialization script before first paint; invisible to the server.</summary>
    LocalStorage,
    /// <summary>First-party cookie. Lets server rendering emit the right theme attributes, so no flash even without JavaScript.</summary>
    Cookie,
}

/// <summary>Configuration for <see cref="ThemingServiceCollectionExtensions.AddDesignTokenTheming"/>.</summary>
public sealed class ThemingOptions
{
    /// <summary>The theme catalog. Either set this or <see cref="CatalogFactory"/>.</summary>
    public ThemeCatalog? Catalog { get; set; }
    public Func<IServiceProvider, ThemeCatalog>? CatalogFactory { get; set; }
    public ThemeCssOptions Css { get; set; } = ThemeCssOptions.Default;
    /// <summary>Element that receives theme attributes and inline overrides. Default <c>:root</c> (the html element).</summary>
    public string RootSelector { get; set; } = ":root";
    public ThemePersistence Persistence { get; set; } = ThemePersistence.None;
    /// <summary>localStorage key or cookie name.</summary>
    public string StorageKey { get; set; } = "bdt-theme";
    /// <summary>Cookie lifetime. Default one year.</summary>
    public TimeSpan CookieLifetime { get; set; } = TimeSpan.FromDays(365);
    /// <summary>Track operating-system color-scheme and contrast preferences for axes set to <c>system</c>.</summary>
    public bool FollowSystemPreferences { get; set; } = true;
}

public static class ThemingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the theme catalog and stylesheet (singletons) and a scoped <see cref="ThemeManager"/> (one per circuit or app).
    /// </summary>
    public static IServiceCollection AddDesignTokenTheming(this IServiceCollection services, Action<ThemingOptions> configure)
    {
        var options = new ThemingOptions();
        configure(options);
        if (options.Catalog is null && options.CatalogFactory is null) throw new InvalidOperationException("Set ThemingOptions.Catalog or ThemingOptions.CatalogFactory.");
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp => options.Catalog ?? options.CatalogFactory!(sp));
        services.TryAddSingleton(sp => new ThemeStylesheetCache(sp.GetRequiredService<ThemeCatalog>(), options.Css));
        services.TryAddScoped<ThemeBrowserInterop>();
        services.TryAddScoped<IThemePreferenceStore>(sp => options.Persistence switch
        {
            ThemePersistence.LocalStorage => new LocalStorageThemePreferenceStore(sp.GetRequiredService<ThemeBrowserInterop>(), options.StorageKey),
            ThemePersistence.Cookie => new CookieThemePreferenceStore(sp.GetRequiredService<ThemeBrowserInterop>(), options.StorageKey, options.CookieLifetime),
            _ => NullThemePreferenceStore.Instance,
        });
        services.TryAddScoped(sp => new ThemeManager(sp.GetRequiredService<ThemeCatalog>(), sp.GetRequiredService<IThemePreferenceStore>()));
        return services;
    }
}

/// <summary>Generates the theme stylesheet once per application and keeps it for every render.</summary>
public sealed class ThemeStylesheetCache(ThemeCatalog catalog, ThemeCssOptions options)
{
    private readonly Lazy<ThemeStylesheetResult> _result = new(() => ThemeStylesheetGenerator.Generate(catalog, options with { IncludeHeader = false }));

    public ThemeCssOptions Options { get; } = options;
    public ThemeStylesheetResult Result => _result.Value;
    public string Css => Result.Css;
}

/// <summary>
/// Thin wrapper around the isolated <c>bdt-theme.js</c> module. Calls are only made after the first interactive render;
/// disconnections (server circuits) and prerendering are tolerated.
/// </summary>
public sealed class ThemeBrowserInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/BlazorDesignToolkit.Theming.Blazor/bdt-theme.js";
    private Task<IJSObjectReference>? _module;
    private bool _disposed;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask<bool> ApplyAsync(string rootSelector, IReadOnlyDictionary<string, string?> attributes, IReadOnlyDictionary<string, string?> properties,
        IReadOnlyCollection<string> clearProperties, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<bool>("apply", cancellationToken, rootSelector, attributes, properties, clearProperties);

    public async ValueTask<IReadOnlyDictionary<string, string?>> ReadAttributesAsync(string rootSelector, IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<Dictionary<string, string?>>("readAttributes", cancellationToken, rootSelector, names) ?? [];

    public async ValueTask<SystemPreferences> GetSystemPreferencesAsync(CancellationToken cancellationToken = default)
        => ToPreferences(await SafeInvokeAsync<JsonElement?>("getSystemPreferences", cancellationToken));

    public async ValueTask<int> WatchSystemPreferencesAsync<T>(DotNetObjectReference<T> reference) where T : class
        => await SafeInvokeAsync<int>("watchSystemPreferences", default, reference);

    public async ValueTask UnwatchAsync(int id)
    {
        if (id != 0) await SafeInvokeAsync<object?>("unwatch", default, id);
    }

    public async ValueTask<string?> LoadStoredPreferenceAsync(string key, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<string?>("loadStoredPreference", cancellationToken, key);

    public async ValueTask StorePreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<bool>("storePreference", cancellationToken, key, value);

    public async ValueTask ClearStoredPreferenceAsync(string key, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<bool>("clearStoredPreference", cancellationToken, key);

    public async ValueTask<string?> ReadCookieAsync(string name, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<string?>("readPreferenceCookie", cancellationToken, name);

    public async ValueTask WriteCookieAsync(string name, string? value, TimeSpan lifetime, CancellationToken cancellationToken = default)
        => await SafeInvokeAsync<bool>("writePreferenceCookie", cancellationToken, name, value, (long)lifetime.TotalSeconds);

    internal static SystemPreferences ToPreferences(JsonElement? json)
    {
        if (json is not { ValueKind: JsonValueKind.Object } e) return SystemPreferences.Unknown;
        static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        return new SystemPreferences
        {
            ColorScheme = Str(e, "colorScheme"),
            Contrast = Str(e, "contrast"),
            ForcedColors = Bool(e, "forcedColors"),
            ReducedMotion = Bool(e, "reducedMotion"),
        };
    }

    private async ValueTask<TValue?> SafeInvokeAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        if (_disposed) return default;
        try
        {
            var module = await Module;
            return await module.InvokeAsync<TValue>(identifier, cancellationToken, args);
        }
        catch (JSDisconnectedException) { return default; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return default; }
        catch (InvalidOperationException) { return default; } // prerendering: JS interop is not available yet
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_module is null) return;
        try
        {
            var module = await _module;
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
        catch (TaskCanceledException) { }
    }
}

/// <summary>Persists the preference in localStorage (read by the early-initialization script).</summary>
public sealed class LocalStorageThemePreferenceStore(ThemeBrowserInterop interop, string key) : IThemePreferenceStore
{
    public async ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default)
        => ThemePreference.TryParse(await interop.LoadStoredPreferenceAsync(key, cancellationToken), out var p) ? p : null;

    public ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default) => interop.StorePreferenceAsync(key, preference.Format(), cancellationToken);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => interop.ClearStoredPreferenceAsync(key, cancellationToken);
}

/// <summary>Persists the preference in a first-party cookie so the server can render the right theme attributes.</summary>
public sealed class CookieThemePreferenceStore(ThemeBrowserInterop interop, string name, TimeSpan lifetime) : IThemePreferenceStore
{
    public async ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default)
        => ThemePreference.TryParse(await interop.ReadCookieAsync(name, cancellationToken), out var p) ? p : null;

    public ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default) => interop.WriteCookieAsync(name, preference.Format(), lifetime, cancellationToken);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => interop.WriteCookieAsync(name, null, TimeSpan.Zero, cancellationToken);
}

/// <summary>Helpers for server rendering.</summary>
public static class ThemeRootAttributes
{
    /// <summary>
    /// Attributes for the root element (for example <c>&lt;html @attributes="..."&gt;</c>) so that the first server-rendered
    /// response already carries the right theme. Use with a preference read from the request cookie.
    /// </summary>
    public static IReadOnlyDictionary<string, object> For(ThemeCatalog catalog, ThemeSelection? selection, ThemeCssOptions? options = null)
    {
        options ??= ThemeCssOptions.Default;
        var normalized = catalog.Normalize(selection ?? catalog.DefaultSelection, out _);
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal) { [options.ThemeAttribute] = normalized.ThemeId };
        foreach (var (axis, value) in normalized.Axes) attributes[options.AxisAttribute(axis)] = value;
        return attributes;
    }

    /// <summary>Parses a preference cookie value written by <see cref="CookieThemePreferenceStore"/>.</summary>
    public static ThemeSelection? FromCookie(string? cookieValue)
        => ThemePreference.TryParse(cookieValue is null ? null : Uri.UnescapeDataString(cookieValue), out var p) ? new ThemeSelection(p.ThemeId, p.Axes) : null;

    internal static string InitConfigJson(ThemeCatalog catalog, ThemingOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("storage", options.Persistence switch { ThemePersistence.LocalStorage => "localStorage", ThemePersistence.Cookie => "cookie", _ => "none" });
            writer.WriteString("key", options.StorageKey);
            writer.WriteString("themeAttribute", options.Css.ThemeAttribute);
            writer.WriteString("axisPrefix", options.Css.AxisAttributePrefix);
            writer.WriteStartArray("themes");
            foreach (var t in catalog.Themes) writer.WriteStringValue(t.Id);
            writer.WriteEndArray();
            writer.WriteStartObject("axes");
            foreach (var axis in catalog.Axes)
            {
                writer.WriteStartArray(axis.Name);
                foreach (var v in axis.Values) writer.WriteStringValue(v);
                if (axis.SupportsSystem) writer.WriteStringValue(ThemeAxes.System);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
