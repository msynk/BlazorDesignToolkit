using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Export;
using BlazorDesignToolkit.Tokens.Json;
using BlazorDesignToolkit.Tokens.Resolution;
using BlazorDesignToolkit.Tokens.Values;

namespace BlazorDesignToolkit.Tokens.Tailwind;

/// <summary>Module syntax of the generated Tailwind 3 preset.</summary>
public enum TailwindModuleFormat
{
    /// <summary><c>module.exports = { … }</c> — load with <c>presets: [require('./tokens.tailwind.cjs')]</c>.</summary>
    CommonJs,
    /// <summary><c>export default { … }</c> — for ESM configs (<c>tailwind.config.mjs</c>).</summary>
    EsModule,
}

public sealed record TailwindConfigExportOptions
{
    /// <summary>The same mappings as the v4 exporter; namespaces use Tailwind v4 names and are translated (see <see cref="TailwindConfigExporter.ThemeKeys"/>).</summary>
    public IReadOnlyList<TailwindMapping> Mappings { get; init; } = [];
    public IntegrationValueMode Values { get; init; } = IntegrationValueMode.Reference;
    public CssExportOptions Css { get; init; } = CssExportOptions.Default;
    public TailwindModuleFormat Module { get; init; } = TailwindModuleFormat.CommonJs;
    /// <summary>
    /// Write colors as <c>color-mix(in oklab, … calc(&lt;alpha-value&gt; * 100%), transparent)</c> so that Tailwind 3 opacity
    /// modifiers (<c>bg-primary/50</c>) work with runtime variables and any color space. Default true.
    /// </summary>
    public bool OpacityModifiers { get; init; } = true;
    /// <summary>Put values under <c>theme.extend</c> (default, keeps Tailwind's defaults) instead of replacing <c>theme</c> keys.</summary>
    public bool Extend { get; init; } = true;
    public bool IncludeHeader { get; init; } = true;
}

/// <summary>
/// Generates a Tailwind CSS 3 preset (a JavaScript module with <c>theme.extend</c>) from tokens, using the same type-checked
/// mappings as <see cref="TailwindThemeExporter"/>. In reference mode values are <c>var(--…)</c> references to the toolkit's
/// runtime variables, so utilities follow theme and mode switches; screens and container widths are always literal because
/// Tailwind places them in media queries. The output is JSON-encoded inside the module, so token text cannot inject code.
/// </summary>
public static class TailwindConfigExporter
{
    /// <summary>Tailwind 3 theme keys for the v4 namespace names accepted in mappings. Namespaces without a v3 key are rejected.</summary>
    public static IReadOnlyDictionary<string, string> ThemeKeys { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["color"] = "colors",
        ["font"] = "fontFamily",
        ["text"] = "fontSize",
        ["font-weight"] = "fontWeight",
        ["tracking"] = "letterSpacing",
        ["leading"] = "lineHeight",
        ["spacing"] = "spacing",
        ["radius"] = "borderRadius",
        ["shadow"] = "boxShadow",
        ["drop-shadow"] = "dropShadow",
        ["blur"] = "blur",
        ["breakpoint"] = "screens",
        ["container"] = "maxWidth",
        ["ease"] = "transitionTimingFunction",
    };

    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        Indented = true,
        // The output is a JavaScript file, not HTML: keep '<alpha-value>' and quotes readable. Strings are still escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TokenExportOutput Export(ResolvedTokenSet tokens, TailwindConfigExportOptions options)
    {
        var bag = new DiagnosticBag();
        var v4 = new TailwindExportOptions { Values = options.Values, Css = options.Css };
        if (!TailwindThemeExporter.CheckPrefix(v4, bag)) return new TokenExportOutput("", bag.ToSortedList());

        var theme = new SortedDictionary<string, List<KeyValuePair<string, object>>>(StringComparer.Ordinal);
        foreach (var mapping in options.Mappings)
        {
            if (!TailwindThemeExporter.Namespaces.ContainsKey(mapping.Namespace))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"'{mapping.Namespace}' is not a supported Tailwind theme namespace.",
                    expected: string.Join(", ", ThemeKeys.Keys), actual: mapping.Namespace);
                continue;
            }
            if (!ThemeKeys.TryGetValue(mapping.Namespace, out var key))
            {
                bag.Report(DiagnosticCatalog.InvalidMapping, $"Tailwind 3 has no theme key for the v4 namespace '{mapping.Namespace}'.",
                    expected: string.Join(", ", ThemeKeys.Keys), actual: mapping.Namespace,
                    remediation: "Remove the mapping for Tailwind 3, or use the Tailwind 4 output.");
                continue;
            }
            if (!theme.TryGetValue(key, out var entries)) theme[key] = entries = [];
            foreach (var (token, suffix) in TailwindThemeExporter.Match(tokens, mapping, bag))
            {
                var name = suffix.Length == 0 ? "DEFAULT" : suffix;
                if (entries.Any(e => e.Key == name))
                {
                    bag.Report(DiagnosticCatalog.NameCollision, $"'{token.Path}' maps to the existing Tailwind 3 key {key}.{name}.", token.Path.ToString(), token.Location);
                    continue;
                }
                if (Value(token, mapping.Namespace, options, bag) is { } value) entries.Add(new(name, value));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, s_writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("theme");
            if (options.Extend) writer.WriteStartObject("extend");
            foreach (var (key, entries) in theme)
            {
                writer.WriteStartObject(key);
                foreach (var (name, value) in entries)
                {
                    writer.WritePropertyName(name);
                    Write(writer, value);
                }
                writer.WriteEndObject();
            }
            if (options.Extend) writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);

        var sb = new StringBuilder();
        if (options.IncludeHeader)
        {
            sb.Append("// Generated by BlazorDesignToolkit for Tailwind CSS 3. Do not edit by hand.\n");
            if (options.Values == IntegrationValueMode.Reference)
                sb.Append("// Values reference the runtime token variables; load the token stylesheet on every page.\n");
        }
        sb.Append("/** @type {import('tailwindcss').Config} */\n");
        sb.Append(options.Module == TailwindModuleFormat.EsModule ? "export default " : "module.exports = ");
        sb.Append(json).Append(";\n");
        return new TokenExportOutput(sb.ToString(), bag.ToSortedList());
    }

    private static object? Value(ResolvedToken token, string ns, TailwindConfigExportOptions options, DiagnosticBag bag)
    {
        var mode = TailwindThemeExporter.LiteralOnlyNamespaces.Contains(ns) ? IntegrationValueMode.Literal : options.Values;
        var reference = mode == IntegrationValueMode.Reference;
        if (token.Value is TypographyValue typography && ns == "text")
        {
            // fontSize: [size, { lineHeight, letterSpacing, fontWeight }]
            var baseName = CssExporter.GetVariableName(token.Path, options.Css);
            return new object[]
            {
                reference ? $"var({baseName}-font-size)" : CssExporter.FormatValue(typography.FontSize, options.Css)!,
                new KeyValuePair<string, object>[]
                {
                    new("lineHeight", reference ? $"var({baseName}-line-height)" : CssExporter.FormatNumber(typography.LineHeight)),
                    new("letterSpacing", reference ? $"var({baseName}-letter-spacing)" : CssExporter.FormatValue(typography.LetterSpacing, options.Css)!),
                    new("fontWeight", reference ? $"var({baseName}-font-weight)" : CssExporter.FormatNumber(typography.FontWeight.Weight)),
                },
            };
        }
        if (ns == "drop-shadow" && token.Value is ShadowValue shadow && shadow.Layers.Any(static l => l.Inset || l.Spread.Value != 0))
        {
            bag.Report(DiagnosticCatalog.UnsupportedConversion, $"'{token.Path}' uses inset or spread, which drop-shadow() cannot express.", token.Path.ToString(), token.Location);
            return null;
        }
        // drop-shadow() takes no spread, so it is always written literally (as the v4 exporter does).
        if (ns == "drop-shadow" && token.Value is ShadowValue dropShadow)
        {
            return string.Join(", ", dropShadow.Layers.Select(l =>
                $"{CssExporter.FormatValue(l.OffsetX, options.Css)} {CssExporter.FormatValue(l.OffsetY, options.Css)} {CssExporter.FormatValue(l.Blur, options.Css)} {CssExporter.FormatValue(l.Color, options.Css)}"));
        }
        var value = IntegrationSupport.Format(token, mode, options.Css, bag);
        if (value is null) return null;
        if (ns == "color" && options.OpacityModifiers) return $"color-mix(in oklab, {value} calc(<alpha-value> * 100%), transparent)";
        return value;
    }

    private static void Write(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case object[] array:
                writer.WriteStartArray();
                foreach (var item in array) Write(writer, item);
                writer.WriteEndArray();
                break;
            case KeyValuePair<string, object>[] obj:
                writer.WriteStartObject();
                foreach (var (k, v) in obj)
                {
                    writer.WritePropertyName(k);
                    Write(writer, v);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException($"Unexpected value {value.GetType()}.");
        }
    }
}

/// <summary><see cref="ITokenExporter"/> adapter <c>tailwind-v3</c>. Configuration: the same <c>"mappings"</c> as <c>tailwind</c>.</summary>
public sealed class TailwindConfigTokenExporter : ITokenExporter
{
    public string Format => "tailwind-v3";
    public string Description => "Tailwind CSS 3 preset (JavaScript theme.extend) with the same type-checked namespace mappings.";
    public IReadOnlyDictionary<string, string> SupportedSettings { get; } = new Dictionary<string, string>
    {
        ["prefix"] = "Prefix of the runtime token variables referenced in reference mode.",
        ["values"] = "'reference' (default, var()) or 'literal'.",
        ["module"] = "'common-js' (default, module.exports) or 'es-module' (export default).",
        ["opacityModifiers"] = "Wrap colors in color-mix() with <alpha-value> so bg-x/50 works. Default true.",
        ["extend"] = "Put values under theme.extend. Default true.",
        ["colorFormat"] = "Color format for literal mode: 'auto', 'hex' or 'native'.",
    };

    public TokenExportOutput Export(TokenExportContext context)
    {
        var bag = new DiagnosticBag();
        var mappings = TailwindTokenExporter.ReadMappings(context, bag);
        var output = TailwindConfigExporter.Export(context.Tokens, new TailwindConfigExportOptions
        {
            Mappings = mappings,
            Values = context.GetEnum("values", IntegrationValueMode.Reference),
            Module = context.GetEnum("module", TailwindModuleFormat.CommonJs),
            OpacityModifiers = context.GetBool("opacityModifiers", true),
            Extend = context.GetBool("extend", true),
            Css = CssExportOptions.Default with { Prefix = context.Get("prefix") ?? "", ColorFormat = context.GetEnum("colorFormat", CssColorFormat.Auto) },
        });
        return new TokenExportOutput(output.Content, [.. bag.ToSortedList(), .. output.Diagnostics]);
    }
}
