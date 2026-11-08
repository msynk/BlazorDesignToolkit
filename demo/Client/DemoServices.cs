using System.Reflection;
using BlazorDesignToolkit.Primitives;
using BlazorDesignToolkit.Theming;
using BlazorDesignToolkit.Theming.Blazor;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorDesignToolkit.Demo.Client;

/// <summary>The sample "Aurora" design system, loaded from embedded token files so both hosting modes share it.</summary>
public static class DemoTokens
{
    public const string Prefix = "ds";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> s_resources = new(() =>
    {
        var assembly = typeof(DemoTokens).Assembly;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            map[name.Replace('\\', '/')] = reader.ReadToEnd();
        }
        return map;
    });

    /// <summary>Token files keyed by path relative to the tokens folder (e.g. "brands/ember.tokens.json").</summary>
    public static IReadOnlyDictionary<string, string> Files { get; } = new LazyFiles("Tokens/");

    public static IReadOnlyDictionary<string, string> Samples { get; } = new LazyFiles("Samples/");

    public static IReadOnlyDictionary<string, string> Sources { get; } = new LazyFiles("Sources/");

    private static readonly Lazy<ThemeCatalog> s_catalog = new(() =>
    {
        var root = (SourceObject)SourceJsonParser.Parse(Files["aurora.themes.json"]).Root!;
        // The in-memory loader completes synchronously, so this does not block on asynchronous work.
        var result = ThemeManifest.LoadAsync(root, null, new InMemoryResolverSourceLoader(Files)).GetAwaiter().GetResult();
        return result.Catalog ?? throw new InvalidOperationException("The sample theme manifest is invalid:\n" + string.Join("\n", result.Diagnostics));
    });

    public static ThemeCatalog Catalog => s_catalog.Value;

    private static readonly Lazy<ValidationOptions> s_validation = new(() =>
    {
        var root = (SourceObject)SourceJsonParser.Parse(Files["aurora.validation.json"]).Root!;
        return ValidationConfig.Read(root, new DiagnosticBag());
    });

    public static ValidationOptions Validation => s_validation.Value;

    public static ThemeCssOptions CssOptions { get; } = ThemeCssOptions.Default with
    {
        Css = ThemeCssOptions.Default.Css with { Prefix = Prefix },
        Layer = "tokens",
    };

    public static CssExportOptions Css => CssOptions.Css;

    private sealed class LazyFiles(string prefix) : IReadOnlyDictionary<string, string>
    {
        private Dictionary<string, string> Map => s_map ??= s_resources.Value.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
        private Dictionary<string, string>? s_map;
        public string this[string key] => Map[key];
        public IEnumerable<string> Keys => Map.Keys;
        public IEnumerable<string> Values => Map.Values;
        public int Count => Map.Count;
        public bool ContainsKey(string key) => Map.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => Map.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Map.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Limits shared by the server host and the pages.</summary>
public static class DemoLimits
{
    /// <summary>Largest token document the import page accepts (pasted or opened from a file).</summary>
    public const int MaxImportBytes = 2 * 1024 * 1024;
}

public static class DemoServiceCollectionExtensions
{
    /// <summary>Services shared by the server host and the WebAssembly client.</summary>
    public static IServiceCollection AddDemoServices(this IServiceCollection services)
    {
        services.AddDesignTokenTheming(options =>
        {
            options.CatalogFactory = _ => DemoTokens.Catalog;
            options.Css = DemoTokens.CssOptions;
            // A cookie lets the server render the selected theme on the first response (no flash, works without JS).
            options.Persistence = ThemePersistence.Cookie;
            options.StorageKey = "bdt-theme";
        });
        services.AddHeadlessPrimitives();
        return services;
    }

    /// <summary>Package version without build metadata (the "+commit" suffix).</summary>
    internal static string Version { get; } = typeof(DemoTokens).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";
}
