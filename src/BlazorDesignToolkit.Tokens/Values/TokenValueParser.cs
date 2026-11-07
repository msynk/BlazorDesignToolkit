using System.Globalization;
using System.Text.RegularExpressions;
using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Values;

/// <summary>Options that affect how values are validated. Defaults follow DTCG 2025.10 strictly.</summary>
public sealed record ValueParseOptions
{
    public static ValueParseOptions Default { get; } = new();

    /// <summary>Extra dimension units (e.g. "em", "%") accepted with a <c>BDT2014</c> warning. DTCG defines only px and rem.</summary>
    public IReadOnlySet<string> AdditionalDimensionUnits { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Accept pre-2025.10 draft formats with a <c>BDT2016</c> warning: hex color strings, "16px"/"1rem" dimension strings,
    /// "200ms"/"0.2s" duration strings. Useful while migrating older token files.
    /// </summary>
    public bool AllowLegacyValueFormats { get; init; }
}

/// <summary>Validates a resolved <c>$value</c> against its type and builds the typed <see cref="TokenValue"/>.</summary>
public sealed partial class TokenValueParser
{
    private static readonly HashSet<string> s_dimensionUnits = new(StringComparer.Ordinal) { "px", "rem" };
    private static readonly HashSet<string> s_durationUnits = new(StringComparer.Ordinal) { "ms", "s" };

    private readonly DiagnosticBag _bag;
    private readonly ValueParseOptions _options;
    private readonly string _tokenPath;
    private bool _failed;

    private TokenValueParser(DiagnosticBag bag, ValueParseOptions options, string tokenPath)
    {
        _bag = bag;
        _options = options;
        _tokenPath = tokenPath;
    }

    /// <summary>Parses <paramref name="value"/> as <paramref name="type"/>. Returns null (and reports diagnostics) when invalid.</summary>
    public static TokenValue? Parse(string type, SourceNode value, string tokenPath, DiagnosticBag bag, ValueParseOptions? options = null)
    {
        var parser = new TokenValueParser(bag, options ?? ValueParseOptions.Default, tokenPath);
        var result = parser.ParseType(type, value, "$value");
        return parser._failed ? null : result;
    }

    private TokenValue? ParseType(string type, SourceNode node, string where) => type switch
    {
        TokenTypes.Color => ParseColor(node, where),
        TokenTypes.Dimension => ParseDimension(node, where),
        TokenTypes.Duration => ParseDuration(node, where),
        TokenTypes.FontFamily => ParseFontFamily(node, where),
        TokenTypes.FontWeight => ParseFontWeight(node, where),
        TokenTypes.CubicBezier => ParseCubicBezier(node, where),
        TokenTypes.Number => ParseNumber(node, where) is { } n ? new NumberValue(n) : null,
        TokenTypes.StrokeStyle => ParseStrokeStyle(node, where),
        TokenTypes.Border => ParseBorder(node, where),
        TokenTypes.Transition => ParseTransition(node, where),
        TokenTypes.Shadow => ParseShadow(node, where),
        TokenTypes.Gradient => ParseGradient(node, where),
        TokenTypes.Typography => ParseTypography(node, where),
        _ => Fail(node, where, $"type '{type}' is not supported", null),
    };

    // ---- Primitive types -------------------------------------------------------------------------------------

    private ColorValue? ParseColor(SourceNode node, string where)
    {
        if (node is SourceString legacy)
        {
            if (_options.AllowLegacyValueFormats && SrgbColor.TryParseHex(legacy.Value, out var c))
            {
                Legacy(node, where, $"hex color string '{legacy.Value}'");
                return ColorValue.FromSrgb(c);
            }
            return Fail<ColorValue>(node, where, $"color must be an object with colorSpace and components, not the string \"{legacy.Value}\"",
                "{\"colorSpace\": \"srgb\", \"components\": [r, g, b]}",
                "DTCG 2025.10 colors are objects. Convert hex strings, or enable AllowLegacyValueFormats while migrating.");
        }
        if (!ExpectObject(node, where, "color", out var obj)) return null;
        CheckUnknown(obj, where, "colorSpace", "components", "alpha", "hex");

        var spaceNode = Required(obj, where, "colorSpace");
        var componentsNode = Required(obj, where, "components");
        if (spaceNode is null || componentsNode is null) return null;
        if (spaceNode is not SourceString { Value: var space } || !ColorSpaces.All.Contains(space))
        {
            return Fail<ColorValue>(spaceNode, $"{where}.colorSpace", "unsupported color space",
                string.Join(", ", ColorSpaces.All.Order(StringComparer.Ordinal)), null, (spaceNode as SourceString)?.Value ?? spaceNode.ToJson());
        }
        if (componentsNode is not SourceArray arr || arr.Items.Count != 3)
        {
            return Fail<ColorValue>(componentsNode, $"{where}.components", $"{space} requires an array of exactly 3 components", "[c1, c2, c3]", null, componentsNode.ToJson());
        }
        var components = new double?[3];
        var ranges = ColorSpaces.GetRanges(space);
        for (var i = 0; i < 3; i++)
        {
            var item = arr.Items[i];
            if (item is SourceString { Value: "none" }) { components[i] = null; continue; }
            if (item is not SourceNumber n)
            {
                return Fail<ColorValue>(item, $"{where}.components[{i}]", "each component must be a number or the keyword \"none\"", "number | \"none\"", null, item.ToJson());
            }
            components[i] = n.Value;
            var (min, max, exclusive) = ranges[i];
            if (n.Value < min || n.Value > max || (exclusive && n.Value >= max))
            {
                _bag.Report(DiagnosticCatalog.ValueOutOfRange,
                    $"Component {i} of {space} color '{_tokenPath}' is {Fmt(n.Value)}, outside {Range(min, max, exclusive)}.",
                    _tokenPath, item.Location, expected: Range(min, max, exclusive), actual: Fmt(n.Value));
            }
        }

        double alpha = 1;
        if (obj.TryGetProperty("alpha", out var alphaNode))
        {
            if (alphaNode is not SourceNumber a || a.Value < 0 || a.Value > 1)
            {
                return Fail<ColorValue>(alphaNode, $"{where}.alpha", "alpha must be a number between 0 and 1", "[0, 1]", null, alphaNode.ToJson());
            }
            alpha = a.Value;
        }

        string? hex = null;
        if (obj.TryGetProperty("hex", out var hexNode))
        {
            if (hexNode is not SourceString hs || !HexPattern().IsMatch(hs.Value))
            {
                return Fail<ColorValue>(hexNode, $"{where}.hex", "hex must use 6-digit CSS hex notation (alpha belongs in 'alpha')", "#rrggbb", null, hexNode.ToJson());
            }
            hex = hs.Value;
        }

        var color = new ColorValue(space, components, alpha, hex);
        if (hex is not null && !color.HasNoneComponent)
        {
            var srgb = color.ToSrgb();
            SrgbColor.TryParseHex(hex, out var declared);
            if (srgb.IsInGamut && (Math.Abs(SrgbColor.To8(srgb.R) - SrgbColor.To8(declared.R)) > 1
                || Math.Abs(SrgbColor.To8(srgb.G) - SrgbColor.To8(declared.G)) > 1
                || Math.Abs(SrgbColor.To8(srgb.B) - SrgbColor.To8(declared.B)) > 1))
            {
                _bag.Report(DiagnosticCatalog.HexMismatch, $"Hex fallback {hex} of '{_tokenPath}' does not match its {space} components ({srgb.ToHex(false)}).",
                    _tokenPath, hexNode!.Location, expected: srgb.ToHex(false), actual: hex);
            }
        }
        return color;
    }

    private DimensionValue? ParseDimension(SourceNode node, string where)
    {
        if (node is SourceString s)
        {
            var m = LegacyDimension().Match(s.Value);
            if (_options.AllowLegacyValueFormats && m.Success && IsDimensionUnit(m.Groups[2].Value, node, where, report: false))
            {
                Legacy(node, where, $"dimension string '{s.Value}'");
                return new DimensionValue(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value);
            }
            return Fail<DimensionValue>(node, where, $"dimension must be an object {{\"value\", \"unit\"}}, not the string \"{s.Value}\"", "{\"value\": 16, \"unit\": \"px\"}", null, s.Value);
        }
        if (!ExpectObject(node, where, "dimension", out var obj)) return null;
        CheckUnknown(obj, where, "value", "unit");
        var value = RequiredNumber(obj, where, "value");
        var unitNode = Required(obj, where, "unit");
        if (value is null || unitNode is null) return null;
        if (unitNode is not SourceString { Value: var unit }) return Fail<DimensionValue>(unitNode, $"{where}.unit", "unit must be a string", "\"px\" | \"rem\"", null, unitNode.ToJson());
        return IsDimensionUnit(unit, unitNode, $"{where}.unit", report: true) ? new DimensionValue(value.Value, unit) : null;
    }

    private bool IsDimensionUnit(string unit, SourceNode node, string where, bool report)
    {
        if (s_dimensionUnits.Contains(unit)) return true;
        if (_options.AdditionalDimensionUnits.Contains(unit))
        {
            if (report)
            {
                _bag.Report(DiagnosticCatalog.NonStandardUnit, $"Dimension unit '{unit}' of '{_tokenPath}' is not defined by DTCG 2025.10.",
                    _tokenPath, node.Location, expected: "px | rem", actual: unit);
            }
            return true;
        }
        if (report)
        {
            Fail(node, where, $"unsupported dimension unit '{unit}'", "px | rem", "DTCG 2025.10 defines only 'px' and 'rem'. Other units can be allowed through AdditionalDimensionUnits.", unit);
        }
        return false;
    }

    private DurationValue? ParseDuration(SourceNode node, string where)
    {
        if (node is SourceString s)
        {
            var m = LegacyDuration().Match(s.Value);
            if (_options.AllowLegacyValueFormats && m.Success)
            {
                Legacy(node, where, $"duration string '{s.Value}'");
                return new DurationValue(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value);
            }
            return Fail<DurationValue>(node, where, $"duration must be an object {{\"value\", \"unit\"}}, not the string \"{s.Value}\"", "{\"value\": 200, \"unit\": \"ms\"}", null, s.Value);
        }
        if (!ExpectObject(node, where, "duration", out var obj)) return null;
        CheckUnknown(obj, where, "value", "unit");
        var value = RequiredNumber(obj, where, "value");
        var unitNode = Required(obj, where, "unit");
        if (value is null || unitNode is null) return null;
        if (unitNode is not SourceString { Value: var unit } || !s_durationUnits.Contains(unit))
        {
            return Fail<DurationValue>(unitNode, $"{where}.unit", "duration unit must be \"ms\" or \"s\"", "ms | s", null, unitNode.ToJson());
        }
        if (value < 0)
        {
            _bag.Report(DiagnosticCatalog.ValueOutOfRange, $"Duration of '{_tokenPath}' is negative ({Fmt(value.Value)}{unit}).", _tokenPath, obj.Location,
                expected: ">= 0", actual: Fmt(value.Value));
        }
        return new DurationValue(value.Value, unit);
    }

    private FontFamilyValue? ParseFontFamily(SourceNode node, string where)
    {
        switch (node)
        {
            case SourceString s when !string.IsNullOrWhiteSpace(s.Value):
                return new FontFamilyValue([s.Value], isArrayForm: false);
            case SourceArray a when a.Items.Count > 0:
                {
                    var names = new List<string>();
                    for (var i = 0; i < a.Items.Count; i++)
                    {
                        if (a.Items[i] is not SourceString { Value: var name } || string.IsNullOrWhiteSpace(name))
                        {
                            return Fail<FontFamilyValue>(a.Items[i], $"{where}[{i}]", "font family names must be non-empty strings", "string", null, a.Items[i].ToJson());
                        }
                        names.Add(name);
                    }
                    return new FontFamilyValue(names, isArrayForm: true);
                }
            default:
                return Fail<FontFamilyValue>(node, where, "fontFamily must be a non-empty string or a non-empty array of strings", "string | string[]", null, node.ToJson());
        }
    }

    private FontWeightValue? ParseFontWeight(SourceNode node, string where)
    {
        switch (node)
        {
            case SourceNumber n when n.Value is >= 1 and <= 1000:
                return new FontWeightValue(n.Value);
            case SourceNumber n:
                return Fail<FontWeightValue>(node, where, "numeric font weights must be in the range [1, 1000]", "[1, 1000]", null, Fmt(n.Value));
            case SourceString s when FontWeightValue.Keywords.TryGetValue(s.Value, out var w):
                return new FontWeightValue(w, s.Value);
            case SourceString s:
                {
                    var hint = FontWeightValue.Keywords.Keys.FirstOrDefault(k => string.Equals(k, s.Value, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(k.Replace("-", ""), s.Value.Replace("-", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                    return Fail<FontWeightValue>(node, where, $"unknown font weight keyword '{s.Value}' (keywords are case-sensitive)",
                        string.Join(", ", FontWeightValue.Keywords.Keys), hint is null ? null : $"Did you mean '{hint}'?", s.Value);
                }
            default:
                return Fail<FontWeightValue>(node, where, "fontWeight must be a number or a predefined keyword", "number | keyword", null, node.ToJson());
        }
    }

    private CubicBezierValue? ParseCubicBezier(SourceNode node, string where)
    {
        if (node is not SourceArray a || a.Items.Count != 4 || a.Items.Any(static i => i is not SourceNumber))
        {
            return Fail<CubicBezierValue>(node, where, "cubicBezier must be an array of exactly four numbers", "[x1, y1, x2, y2]", null, node.ToJson());
        }
        var v = a.Items.Cast<SourceNumber>().Select(static n => n.Value).ToArray();
        if (v[0] is < 0 or > 1 || v[2] is < 0 or > 1)
        {
            return Fail<CubicBezierValue>(node, where, "the x coordinates (P1x, P2x) of a cubicBezier must be in [0, 1]", "x in [0, 1]", null, node.ToJson());
        }
        return new CubicBezierValue(v[0], v[1], v[2], v[3]);
    }

    private double? ParseNumber(SourceNode node, string where)
    {
        if (node is SourceNumber n) return n.Value;
        Fail(node, where, "expected a JSON number", "number", null, node.ToJson());
        return null;
    }

    // ---- Composite types -------------------------------------------------------------------------------------

    private StrokeStyleValue? ParseStrokeStyle(SourceNode node, string where)
    {
        if (node is SourceString s)
        {
            return StrokeStyleValue.KeywordValues.Contains(s.Value)
                ? new StrokeStyleValue(s.Value)
                : Fail<StrokeStyleValue>(node, where, $"unknown stroke style '{s.Value}'", string.Join(", ", StrokeStyleValue.KeywordValues), null, s.Value);
        }
        if (!ExpectObject(node, where, "strokeStyle", out var obj)) return null;
        CheckComposite(obj, where, "dashArray", "lineCap");
        var dashNode = Required(obj, where, "dashArray");
        var capNode = Required(obj, where, "lineCap");
        if (dashNode is null || capNode is null) return null;
        if (dashNode is not SourceArray dashes || dashes.Items.Count == 0)
        {
            return Fail<StrokeStyleValue>(dashNode, $"{where}.dashArray", "dashArray must be a non-empty array of dimensions", "dimension[]", null, dashNode.ToJson());
        }
        var list = new List<DimensionValue>();
        for (var i = 0; i < dashes.Items.Count; i++)
        {
            if (ParseDimension(dashes.Items[i], $"{where}.dashArray[{i}]") is { } d) list.Add(d);
        }
        if (capNode is not SourceString { Value: var cap } || !StrokeStyleValue.LineCaps.Contains(cap))
        {
            return Fail<StrokeStyleValue>(capNode, $"{where}.lineCap", "lineCap must be \"round\", \"butt\" or \"square\"", "round | butt | square", null, capNode.ToJson());
        }
        return list.Count == dashes.Items.Count ? new StrokeStyleValue(list, cap) : null;
    }

    private BorderValue? ParseBorder(SourceNode node, string where)
    {
        if (!ExpectObject(node, where, "border", out var obj)) return null;
        CheckComposite(obj, where, "color", "width", "style");
        var color = Sub(obj, where, "color", ParseColor);
        var width = Sub(obj, where, "width", ParseDimension);
        var style = Sub(obj, where, "style", ParseStrokeStyle);
        return color is null || width is null || style is null ? null : new BorderValue(color, width, style);
    }

    private TransitionValue? ParseTransition(SourceNode node, string where)
    {
        if (!ExpectObject(node, where, "transition", out var obj)) return null;
        CheckComposite(obj, where, "duration", "delay", "timingFunction");
        var duration = Sub(obj, where, "duration", ParseDuration);
        var delay = Sub(obj, where, "delay", ParseDuration);
        var timing = Sub(obj, where, "timingFunction", ParseCubicBezier);
        return duration is null || delay is null || timing is null ? null : new TransitionValue(duration, delay, timing);
    }

    private ShadowValue? ParseShadow(SourceNode node, string where)
    {
        if (node is SourceArray arr)
        {
            if (arr.Items.Count == 0) return Fail<ShadowValue>(node, where, "a shadow array must contain at least one shadow", "shadow[]", null, "[]");
            var layers = new List<ShadowLayer>();
            for (var i = 0; i < arr.Items.Count; i++)
            {
                if (arr.Items[i] is SourceArray)
                {
                    Fail(arr.Items[i], $"{where}[{i}]",
                        "a shadow array item resolves to another array; DTCG §9.6 states that references do not cause array flattening",
                        "shadow object", "Reference a single-layer shadow token, or inline the layers.", "array");
                    continue;
                }
                if (ParseShadowLayer(arr.Items[i], $"{where}[{i}]") is { } layer) layers.Add(layer);
            }
            return layers.Count == arr.Items.Count ? new ShadowValue(layers, isArrayForm: true) : null;
        }
        return ParseShadowLayer(node, where) is { } single ? new ShadowValue([single], isArrayForm: false) : null;
    }

    private ShadowLayer? ParseShadowLayer(SourceNode node, string where)
    {
        if (!ExpectObject(node, where, "shadow", out var obj)) return null;
        CheckComposite(obj, where, "color", "offsetX", "offsetY", "blur", "spread", "inset");
        var color = Sub(obj, where, "color", ParseColor);
        var x = Sub(obj, where, "offsetX", ParseDimension);
        var y = Sub(obj, where, "offsetY", ParseDimension);
        var blur = Sub(obj, where, "blur", ParseDimension);
        var spread = Sub(obj, where, "spread", ParseDimension);
        var inset = false;
        if (obj.TryGetProperty("inset", out var insetNode))
        {
            if (insetNode is SourceBoolean b) inset = b.Value;
            else Fail(insetNode, $"{where}.inset", "inset must be a boolean", "true | false", null, insetNode.ToJson());
        }
        if (blur is { Value: < 0 })
        {
            Fail(obj, $"{where}.blur", "shadow blur must not be negative", ">= 0", null, Fmt(blur.Value));
        }
        return color is null || x is null || y is null || blur is null || spread is null ? null : new ShadowLayer(color, x, y, blur, spread, inset);
    }

    private GradientValue? ParseGradient(SourceNode node, string where)
    {
        if (node is not SourceArray arr || arr.Items.Count == 0)
        {
            return Fail<GradientValue>(node, where, "gradient must be a non-empty array of stops", "[{color, position}, ...]", null, node.ToJson());
        }
        // References to gradient tokens inside a gradient resolve to arrays; their stops are inserted in place (documented interpretation of §9.7).
        var items = new List<(SourceNode Node, string Where)>();
        for (var i = 0; i < arr.Items.Count; i++)
        {
            if (arr.Items[i] is SourceArray nested)
            {
                for (var j = 0; j < nested.Items.Count; j++) items.Add((nested.Items[j], $"{where}[{i}][{j}]"));
            }
            else items.Add((arr.Items[i], $"{where}[{i}]"));
        }
        var stops = new List<GradientStop>();
        foreach (var (item, itemWhere) in items)
        {
            if (!ExpectObject(item, itemWhere, "gradient stop", out var obj)) continue;
            CheckComposite(obj, itemWhere, "color", "position");
            var color = Sub(obj, itemWhere, "color", ParseColor);
            var posNode = Required(obj, itemWhere, "position");
            if (color is null || posNode is null) continue;
            var position = ParseNumber(posNode, $"{itemWhere}.position");
            if (position is null) continue;
            if (position is < 0 or > 1)
            {
                _bag.Report(DiagnosticCatalog.ValueOutOfRange,
                    $"Gradient stop position {Fmt(position.Value)} of '{_tokenPath}' is outside [0, 1] and is treated as clamped.",
                    _tokenPath, posNode.Location, expected: "[0, 1]", actual: Fmt(position.Value));
            }
            stops.Add(new GradientStop(color, Math.Clamp(position.Value, 0, 1)));
        }
        return stops.Count == items.Count ? new GradientValue(stops) : null;
    }

    private TypographyValue? ParseTypography(SourceNode node, string where)
    {
        if (!ExpectObject(node, where, "typography", out var obj)) return null;
        CheckComposite(obj, where, "fontFamily", "fontSize", "fontWeight", "letterSpacing", "lineHeight");
        var family = Sub(obj, where, "fontFamily", ParseFontFamily);
        var size = Sub(obj, where, "fontSize", ParseDimension);
        var weight = Sub(obj, where, "fontWeight", ParseFontWeight);
        var spacing = Sub(obj, where, "letterSpacing", ParseDimension);
        var lhNode = Required(obj, where, "lineHeight");
        var lineHeight = lhNode is null ? null : ParseNumber(lhNode, $"{where}.lineHeight");
        return family is null || size is null || weight is null || spacing is null || lineHeight is null
            ? null
            : new TypographyValue(family, size, weight, spacing, lineHeight.Value);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private T? Sub<T>(SourceObject obj, string where, string name, Func<SourceNode, string, T?> parse) where T : class
    {
        var node = Required(obj, where, name);
        return node is null ? null : parse(node, $"{where}.{name}");
    }

    private SourceNode? Required(SourceObject obj, string where, string name)
    {
        if (obj.TryGetProperty(name, out var value)) return value;
        Fail(obj, where, $"missing required property '{name}'", name, null, null);
        return null;
    }

    private double? RequiredNumber(SourceObject obj, string where, string name)
    {
        var node = Required(obj, where, name);
        return node is null ? null : ParseNumber(node, $"{where}.{name}");
    }

    private bool ExpectObject(SourceNode node, string where, string what, out SourceObject obj)
    {
        if (node is SourceObject o)
        {
            obj = o;
            return true;
        }
        obj = SourceObject.Empty;
        Fail(node, where, $"{what} value must be an object", "object", null, node.ToJson());
        return false;
    }

    /// <summary>Unknown properties on non-composite value objects are warnings (the color module does not forbid them).</summary>
    private void CheckUnknown(SourceObject obj, string where, params string[] known)
    {
        foreach (var p in obj.Properties)
        {
            if (Array.IndexOf(known, p.Name) >= 0) continue;
            _bag.Report(DiagnosticCatalog.UnknownValueProperty, $"Unknown property '{p.Name}' in {where} of '{_tokenPath}'.", _tokenPath, p.NameLocation,
                expected: string.Join(", ", known), actual: p.Name);
        }
    }

    /// <summary>Composite tokens with extra sub-values are invalid (DTCG §9.2).</summary>
    private void CheckComposite(SourceObject obj, string where, params string[] known)
    {
        foreach (var p in obj.Properties)
        {
            if (Array.IndexOf(known, p.Name) >= 0) continue;
            Fail(obj, where, $"unexpected sub-value '{p.Name}' (composite values must not contain additional sub-values)", string.Join(", ", known), null, p.Name);
        }
    }

    private void Legacy(SourceNode node, string where, string what)
        => _bag.Report(DiagnosticCatalog.LegacyValueFormat, $"{where} of '{_tokenPath}' uses a legacy {what}.", _tokenPath, node.Location, actual: node.ToJson());

    private T? Fail<T>(SourceNode node, string where, string message, string? expected, string? remediation = null, string? actual = null) where T : class
    {
        Fail(node, where, message, expected, remediation, actual);
        return null;
    }

    private TokenValue? Fail(SourceNode node, string where, string message, string? remediation) => Fail<TokenValue>(node, where, message, null, remediation);

    private void Fail(SourceNode node, string where, string message, string? expected, string? remediation, string? actual)
    {
        _failed = true;
        _bag.Report(DiagnosticCatalog.InvalidValue, $"Invalid {where} of '{_tokenPath}': {message}.", _tokenPath, node.Location, expected, actual, remediation);
    }

    private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Range(double min, double max, bool exclusive)
        => $"[{(double.IsNegativeInfinity(min) ? "-inf" : Fmt(min))}, {(double.IsPositiveInfinity(max) ? "inf" : Fmt(max))}{(exclusive ? ")" : "]")}";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^\s*(-?\d+(?:\.\d+)?)\s*([a-z%]+)\s*$")]
    private static partial Regex LegacyDimension();

    [GeneratedRegex(@"^\s*(-?\d+(?:\.\d+)?)\s*(ms|s)\s*$")]
    private static partial Regex LegacyDuration();
}
