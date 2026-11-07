using BlazorDesignToolkit.Tokens.Colors;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Values;

/// <summary>A validated, fully resolved token value of one of the DTCG 2025.10 types.</summary>
public abstract class TokenValue
{
    public abstract string Type { get; }

    /// <summary>Canonical DTCG JSON representation of this value.</summary>
    public abstract SourceNode ToSourceNode();

    public override string ToString() => ToSourceNode().ToJson();

    public override bool Equals(object? obj) => obj is TokenValue other && Type == other.Type && SourceNode.DeepEquals(ToSourceNode(), other.ToSourceNode());

    public override int GetHashCode() => HashCode.Combine(Type, ToSourceNode().ToJson());

    protected static SourceNumber Num(double v) => new(v);
}

public sealed class ColorValue : TokenValue
{
    public ColorValue(string colorSpace, IReadOnlyList<double?> components, double alpha = 1, string? hex = null)
    {
        if (!ColorSpaces.All.Contains(colorSpace)) throw new ArgumentException($"Unsupported color space '{colorSpace}'.", nameof(colorSpace));
        if (components.Count != 3) throw new ArgumentException("Exactly three components are required.", nameof(components));
        ColorSpace = colorSpace;
        Components = [.. components];
        Alpha = alpha;
        Hex = hex;
    }

    public static ColorValue FromSrgb(SrgbColor c, bool includeHex = true)
        => new(ColorSpaces.Srgb, [c.R, c.G, c.B], c.A, includeHex && c.IsInGamut ? c.ToHex(includeAlpha: false) : null);

    public static ColorValue FromHex(string hex)
        => SrgbColor.TryParseHex(hex, out var c) ? FromSrgb(c) : throw new FormatException($"'{hex}' is not a hex color.");

    public override string Type => TokenTypes.Color;
    public string ColorSpace { get; }
    /// <summary>Components; null represents the 'none' keyword.</summary>
    public IReadOnlyList<double?> Components { get; }
    public double Alpha { get; }
    /// <summary>Optional 6-digit hex fallback (never includes alpha).</summary>
    public string? Hex { get; }
    public bool HasNoneComponent => Components.Any(static c => c is null);

    /// <summary>Gamma-encoded sRGB (unclipped; check <see cref="SrgbColor.IsInGamut"/>).</summary>
    public SrgbColor ToSrgb() => ColorMath.ToSrgb(ColorSpace, Components, Alpha);

    /// <summary>sRGB within gamut: unchanged when representable, otherwise CSS Color 4 gamut-mapped (see <see cref="ColorMath.GamutMapToSrgb"/>).</summary>
    public SrgbColor ToSrgbGamutMapped() => ColorMath.GamutMapToSrgb(ColorSpace, Components, Alpha);

    public ColorValue ConvertTo(string colorSpace)
    {
        var converted = ColorMath.Convert(ColorSpace, Components, colorSpace);
        return new ColorValue(colorSpace, [converted[0], converted[1], converted[2]], Alpha, Hex);
    }

    public override SourceNode ToSourceNode()
    {
        var props = new List<(string, SourceNode)>
        {
            ("colorSpace", new SourceString(ColorSpace)),
            ("components", new SourceArray(Components.Select(static c => c is null ? (SourceNode)new SourceString("none") : Num(c.Value)))),
        };
        if (Alpha != 1) props.Add(("alpha", Num(Alpha)));
        if (Hex is not null) props.Add(("hex", new SourceString(Hex)));
        return SourceObject.Create([.. props]);
    }
}

public sealed class DimensionValue(double value, string unit) : TokenValue
{
    public override string Type => TokenTypes.Dimension;
    public double Value { get; } = value;
    public string Unit { get; } = unit;
    public override SourceNode ToSourceNode() => SourceObject.Create(("value", Num(Value)), ("unit", new SourceString(Unit)));
}

public sealed class DurationValue(double value, string unit) : TokenValue
{
    public override string Type => TokenTypes.Duration;
    public double Value { get; } = value;
    public string Unit { get; } = unit;
    public double Milliseconds => Unit == "s" ? Value * 1000 : Value;
    public override SourceNode ToSourceNode() => SourceObject.Create(("value", Num(Value)), ("unit", new SourceString(Unit)));
}

public sealed class FontFamilyValue(IReadOnlyList<string> families, bool isArrayForm) : TokenValue
{
    public override string Type => TokenTypes.FontFamily;
    /// <summary>Font names from most to least preferred.</summary>
    public IReadOnlyList<string> Families { get; } = families;
    public bool IsArrayForm { get; } = isArrayForm;
    public override SourceNode ToSourceNode()
        => IsArrayForm ? new SourceArray(Families.Select(static f => (SourceNode)new SourceString(f))) : new SourceString(Families[0]);
}

public sealed class FontWeightValue(double weight, string? keyword = null) : TokenValue
{
    /// <summary>DTCG 2025.10 §8.4 keyword table.</summary>
    public static IReadOnlyDictionary<string, int> Keywords { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["thin"] = 100, ["hairline"] = 100,
        ["extra-light"] = 200, ["ultra-light"] = 200,
        ["light"] = 300,
        ["normal"] = 400, ["regular"] = 400, ["book"] = 400,
        ["medium"] = 500,
        ["semi-bold"] = 600, ["demi-bold"] = 600,
        ["bold"] = 700,
        ["extra-bold"] = 800, ["ultra-bold"] = 800,
        ["black"] = 900, ["heavy"] = 900,
        ["extra-black"] = 950, ["ultra-black"] = 950,
    };

    public override string Type => TokenTypes.FontWeight;
    public double Weight { get; } = weight;
    public string? Keyword { get; } = keyword;
    public override SourceNode ToSourceNode() => Keyword is not null ? new SourceString(Keyword) : Num(Weight);
}

public sealed class CubicBezierValue(double x1, double y1, double x2, double y2) : TokenValue
{
    public override string Type => TokenTypes.CubicBezier;
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double X2 { get; } = x2;
    public double Y2 { get; } = y2;
    public override SourceNode ToSourceNode() => SourceArray.Create(Num(X1), Num(Y1), Num(X2), Num(Y2));
}

public sealed class NumberValue(double value) : TokenValue
{
    public override string Type => TokenTypes.Number;
    public double Value { get; } = value;
    public override SourceNode ToSourceNode() => Num(Value);
}

public sealed class StrokeStyleValue : TokenValue
{
    public static IReadOnlySet<string> KeywordValues { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "solid", "dashed", "dotted", "double", "groove", "ridge", "outset", "inset" };
    public static IReadOnlySet<string> LineCaps { get; } = new HashSet<string>(StringComparer.Ordinal) { "round", "butt", "square" };

    public StrokeStyleValue(string keyword) => Keyword = keyword;

    public StrokeStyleValue(IReadOnlyList<DimensionValue> dashArray, string lineCap)
    {
        DashArray = dashArray;
        LineCap = lineCap;
    }

    public override string Type => TokenTypes.StrokeStyle;
    public string? Keyword { get; }
    public IReadOnlyList<DimensionValue>? DashArray { get; }
    public string? LineCap { get; }

    public override SourceNode ToSourceNode() => Keyword is not null
        ? new SourceString(Keyword)
        : SourceObject.Create(("dashArray", new SourceArray(DashArray!.Select(static d => d.ToSourceNode()))), ("lineCap", new SourceString(LineCap!)));
}

public sealed class BorderValue(ColorValue color, DimensionValue width, StrokeStyleValue style) : TokenValue
{
    public override string Type => TokenTypes.Border;
    public ColorValue Color { get; } = color;
    public DimensionValue Width { get; } = width;
    public StrokeStyleValue Style { get; } = style;
    public override SourceNode ToSourceNode()
        => SourceObject.Create(("color", Color.ToSourceNode()), ("width", Width.ToSourceNode()), ("style", Style.ToSourceNode()));
}

public sealed class TransitionValue(DurationValue duration, DurationValue delay, CubicBezierValue timingFunction) : TokenValue
{
    public override string Type => TokenTypes.Transition;
    public DurationValue Duration { get; } = duration;
    public DurationValue Delay { get; } = delay;
    public CubicBezierValue TimingFunction { get; } = timingFunction;
    public override SourceNode ToSourceNode()
        => SourceObject.Create(("duration", Duration.ToSourceNode()), ("delay", Delay.ToSourceNode()), ("timingFunction", TimingFunction.ToSourceNode()));
}

public sealed class ShadowLayer(ColorValue color, DimensionValue offsetX, DimensionValue offsetY, DimensionValue blur, DimensionValue spread, bool inset)
{
    public ColorValue Color { get; } = color;
    public DimensionValue OffsetX { get; } = offsetX;
    public DimensionValue OffsetY { get; } = offsetY;
    public DimensionValue Blur { get; } = blur;
    public DimensionValue Spread { get; } = spread;
    public bool Inset { get; } = inset;

    public SourceObject ToSourceNode()
    {
        var props = new List<(string, SourceNode)>
        {
            ("color", Color.ToSourceNode()), ("offsetX", OffsetX.ToSourceNode()), ("offsetY", OffsetY.ToSourceNode()),
            ("blur", Blur.ToSourceNode()), ("spread", Spread.ToSourceNode()),
        };
        if (Inset) props.Add(("inset", new SourceBoolean(true)));
        return SourceObject.Create([.. props]);
    }
}

public sealed class ShadowValue(IReadOnlyList<ShadowLayer> layers, bool isArrayForm) : TokenValue
{
    public override string Type => TokenTypes.Shadow;
    public IReadOnlyList<ShadowLayer> Layers { get; } = layers;
    public bool IsArrayForm { get; } = isArrayForm;
    public override SourceNode ToSourceNode()
        => IsArrayForm || Layers.Count != 1 ? new SourceArray(Layers.Select(static l => (SourceNode)l.ToSourceNode())) : Layers[0].ToSourceNode();
}

public sealed class GradientStop(ColorValue color, double position)
{
    public ColorValue Color { get; } = color;
    /// <summary>Position in [0, 1] (out-of-range values are clamped as required by DTCG §9.7).</summary>
    public double Position { get; } = position;
}

public sealed class GradientValue(IReadOnlyList<GradientStop> stops) : TokenValue
{
    public override string Type => TokenTypes.Gradient;
    public IReadOnlyList<GradientStop> Stops { get; } = stops;
    public override SourceNode ToSourceNode()
        => new SourceArray(Stops.Select(static s => (SourceNode)SourceObject.Create(("color", s.Color.ToSourceNode()), ("position", new SourceNumber(s.Position)))));
}

public sealed class TypographyValue(FontFamilyValue fontFamily, DimensionValue fontSize, FontWeightValue fontWeight, DimensionValue letterSpacing, double lineHeight) : TokenValue
{
    public override string Type => TokenTypes.Typography;
    public FontFamilyValue FontFamily { get; } = fontFamily;
    public DimensionValue FontSize { get; } = fontSize;
    public FontWeightValue FontWeight { get; } = fontWeight;
    public DimensionValue LetterSpacing { get; } = letterSpacing;
    /// <summary>Unitless multiplier of the font size.</summary>
    public double LineHeight { get; } = lineHeight;
    public override SourceNode ToSourceNode() => SourceObject.Create(
        ("fontFamily", FontFamily.ToSourceNode()), ("fontSize", FontSize.ToSourceNode()), ("fontWeight", FontWeight.ToSourceNode()),
        ("letterSpacing", LetterSpacing.ToSourceNode()), ("lineHeight", new SourceNumber(LineHeight)));
}
