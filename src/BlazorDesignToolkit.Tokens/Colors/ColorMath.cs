using System.Collections.Frozen;
using System.Globalization;

namespace BlazorDesignToolkit.Tokens.Colors;

/// <summary>Color-space identifiers of the DTCG 2025.10 color module (§4.2). Identical to CSS Color 4 names.</summary>
public static class ColorSpaces
{
    public const string Srgb = "srgb";
    public const string SrgbLinear = "srgb-linear";
    public const string Hsl = "hsl";
    public const string Hwb = "hwb";
    public const string Lab = "lab";
    public const string Lch = "lch";
    public const string Oklab = "oklab";
    public const string Oklch = "oklch";
    public const string DisplayP3 = "display-p3";
    public const string A98Rgb = "a98-rgb";
    public const string ProphotoRgb = "prophoto-rgb";
    public const string Rec2020 = "rec2020";
    public const string XyzD65 = "xyz-d65";
    public const string XyzD50 = "xyz-d50";

    public static FrozenSet<string> All { get; } = new[]
    {
        Srgb, SrgbLinear, Hsl, Hwb, Lab, Lch, Oklab, Oklch, DisplayP3, A98Rgb, ProphotoRgb, Rec2020, XyzD65, XyzD50,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Documented component ranges (min, max, maxExclusive). Infinite bounds mean "unbounded".</summary>
    public static (double Min, double Max, bool MaxExclusive)[] GetRanges(string space) => space switch
    {
        Hsl or Hwb => [(0, 360, true), (0, 100, false), (0, 100, false)],
        Lab => [(0, 100, false), (double.NegativeInfinity, double.PositiveInfinity, false), (double.NegativeInfinity, double.PositiveInfinity, false)],
        Lch => [(0, 100, false), (0, double.PositiveInfinity, false), (0, 360, true)],
        Oklab => [(0, 1, false), (double.NegativeInfinity, double.PositiveInfinity, false), (double.NegativeInfinity, double.PositiveInfinity, false)],
        Oklch => [(0, 1, false), (0, double.PositiveInfinity, false), (0, 360, true)],
        _ => [(0, 1, false), (0, 1, false), (0, 1, false)],
    };

    /// <summary>Index of the hue component for polar spaces, or -1.</summary>
    public static int HueIndex(string space) => space switch
    {
        Hsl or Hwb => 0,
        Lch or Oklch => 2,
        _ => -1,
    };
}

/// <summary>A gamma-encoded sRGB color with straight (non-premultiplied) alpha. Channels may lie outside [0, 1] when out of gamut.</summary>
public readonly record struct SrgbColor(double R, double G, double B, double A = 1)
{
    /// <summary>Half of an 8-bit step; channels within this tolerance of [0, 1] are treated as in gamut.</summary>
    public const double GamutTolerance = 0.5 / 255;

    public bool IsInGamut => InRange(R) && InRange(G) && InRange(B);

    private static bool InRange(double v) => v >= -GamutTolerance && v <= 1 + GamutTolerance;

    public SrgbColor Clip() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1), Math.Clamp(A, 0, 1));

    /// <summary>Formats as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when alpha &lt; 1 and <paramref name="includeAlpha"/> is true.</summary>
    public string ToHex(bool includeAlpha = true)
    {
        var c = Clip();
        var hex = string.Create(CultureInfo.InvariantCulture, $"#{To8(c.R):x2}{To8(c.G):x2}{To8(c.B):x2}");
        return includeAlpha && To8(c.A) < 255 ? hex + To8(c.A).ToString("x2", CultureInfo.InvariantCulture) : hex;
    }

    public static int To8(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);

    /// <summary>Parses #rgb, #rgba, #rrggbb and #rrggbbaa.</summary>
    public static bool TryParseHex(string? text, out SrgbColor color)
    {
        color = default;
        if (string.IsNullOrEmpty(text) || text[0] != '#') return false;
        var hex = text.AsSpan(1);
        if (hex.Length is 3 or 4)
        {
            Span<char> expanded = stackalloc char[hex.Length * 2];
            for (var i = 0; i < hex.Length; i++) { expanded[2 * i] = hex[i]; expanded[2 * i + 1] = hex[i]; }
            return TryParseHex("#" + expanded.ToString(), out color);
        }
        if (hex.Length is not (6 or 8)) return false;
        if (!int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        var a = 255;
        if (hex.Length == 8 && !int.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) return false;
        color = new SrgbColor(r / 255.0, g / 255.0, b / 255.0, a / 255.0);
        return true;
    }
}

/// <summary>
/// Conversions between the DTCG color spaces, using the CSS Color Module Level 4 reference algorithms
/// (matrices, transfer functions, D50/D65 Bradford adaptation). All conversions go through CIE XYZ (D65).
/// The 'none' keyword is treated as 0, which is how CSS renders missing components.
/// </summary>
public static class ColorMath
{
    // Forward matrices (linear RGB -> XYZ) from CSS Color 4. Inverses are derived numerically so the pair is always consistent.
    private static readonly double[,] s_srgbToXyz =
    {
        { 506752.0 / 1228815, 87881.0 / 245763, 12673.0 / 70218 },
        { 87098.0 / 409605, 175762.0 / 245763, 12673.0 / 175545 },
        { 7918.0 / 409605, 87881.0 / 737289, 1001167.0 / 1053270 },
    };
    private static readonly double[,] s_p3ToXyz =
    {
        { 608311.0 / 1250200, 189793.0 / 714400, 198249.0 / 1000160 },
        { 35783.0 / 156275, 247089.0 / 357200, 198249.0 / 2500400 },
        { 0, 32229.0 / 714400, 5220557.0 / 5000800 },
    };
    private static readonly double[,] s_a98ToXyz =
    {
        { 573536.0 / 994567, 263643.0 / 1420810, 187206.0 / 994567 },
        { 591459.0 / 1989134, 6239551.0 / 9945670, 374412.0 / 4972835 },
        { 53769.0 / 1989134, 351524.0 / 4972835, 4929758.0 / 4972835 },
    };
    private static readonly double[,] s_prophotoToXyzD50 =
    {
        { 0.79776664490064230, 0.13518129740053308, 0.03134773412839220 },
        { 0.28807482881940130, 0.71183523424187300, 0.00008993693872564 },
        { 0.00000000000000000, 0.00000000000000000, 0.82510460251046020 },
    };
    private static readonly double[,] s_rec2020ToXyz =
    {
        { 63426534.0 / 99577255, 20160776.0 / 139408157, 47086771.0 / 278816314 },
        { 26158966.0 / 99577255, 472592308.0 / 697040785, 8267143.0 / 139408157 },
        { 0, 19567812.0 / 697040785, 295819943.0 / 278816314 },
    };
    private static readonly double[,] s_d65ToD50 =
    {
        { 1.0479297925449969, 0.022946870601609652, -0.05019226628920524 },
        { 0.02962780877005599, 0.9904344267538799, -0.017073799063418826 },
        { -0.009243040646204504, 0.015055191490298152, 0.7518742814281371 },
    };
    private static readonly double[,] s_xyzToLms =
    {
        { 0.8190224379967030, 0.3619062600528904, -0.1288737815209879 },
        { 0.0329836539323885, 0.9292868615863434, 0.0361446663506424 },
        { 0.0481771893596242, 0.2642395317527308, 0.6335478284694309 },
    };
    private static readonly double[,] s_lmsToOklab =
    {
        { 0.2104542683093140, 0.7936177747023054, -0.0040720430116193 },
        { 1.9779985324311684, -2.4285922420485799, 0.4505937096174110 },
        { 0.0259040424655478, 0.7827717124575296, -0.8086757549230774 },
    };

    private static readonly double[,] s_xyzToSrgb = Invert(s_srgbToXyz);
    private static readonly double[,] s_xyzToP3 = Invert(s_p3ToXyz);
    private static readonly double[,] s_xyzToA98 = Invert(s_a98ToXyz);
    private static readonly double[,] s_xyzD50ToProphoto = Invert(s_prophotoToXyzD50);
    private static readonly double[,] s_xyzToRec2020 = Invert(s_rec2020ToXyz);
    private static readonly double[,] s_d50ToD65 = Invert(s_d65ToD50);
    private static readonly double[,] s_lmsToXyz = Invert(s_xyzToLms);
    private static readonly double[,] s_oklabToLms = Invert(s_lmsToOklab);

    private static readonly double[] s_d50White = [0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585];
    private const double Kappa = 24389.0 / 27;
    private const double Epsilon = 216.0 / 24389;

    /// <summary>Converts components (null = 'none') in <paramref name="space"/> to CIE XYZ D65.</summary>
    public static double[] ToXyzD65(string space, IReadOnlyList<double?> components)
    {
        if (components.Count != 3) throw new ArgumentException("Exactly three components are required.", nameof(components));
        double a = components[0] ?? 0, b = components[1] ?? 0, c = components[2] ?? 0;
        return space switch
        {
            ColorSpaces.Srgb => Multiply(s_srgbToXyz, [SrgbToLinear(a), SrgbToLinear(b), SrgbToLinear(c)]),
            ColorSpaces.SrgbLinear => Multiply(s_srgbToXyz, [a, b, c]),
            ColorSpaces.Hsl => ToXyzD65(ColorSpaces.Srgb, ToNullable(HslToSrgb(a, b, c))),
            ColorSpaces.Hwb => ToXyzD65(ColorSpaces.Srgb, ToNullable(HwbToSrgb(a, b, c))),
            ColorSpaces.DisplayP3 => Multiply(s_p3ToXyz, [SrgbToLinear(a), SrgbToLinear(b), SrgbToLinear(c)]),
            ColorSpaces.A98Rgb => Multiply(s_a98ToXyz, [A98ToLinear(a), A98ToLinear(b), A98ToLinear(c)]),
            ColorSpaces.ProphotoRgb => Multiply(s_d50ToD65, Multiply(s_prophotoToXyzD50, [ProphotoToLinear(a), ProphotoToLinear(b), ProphotoToLinear(c)])),
            ColorSpaces.Rec2020 => Multiply(s_rec2020ToXyz, [Rec2020ToLinear(a), Rec2020ToLinear(b), Rec2020ToLinear(c)]),
            ColorSpaces.XyzD65 => [a, b, c],
            ColorSpaces.XyzD50 => Multiply(s_d50ToD65, [a, b, c]),
            ColorSpaces.Lab => Multiply(s_d50ToD65, LabToXyzD50(a, b, c)),
            ColorSpaces.Lch => Multiply(s_d50ToD65, LabToXyzD50(a, b * Math.Cos(c * Math.PI / 180), b * Math.Sin(c * Math.PI / 180))),
            ColorSpaces.Oklab => OklabToXyz(a, b, c),
            ColorSpaces.Oklch => OklabToXyz(a, b * Math.Cos(c * Math.PI / 180), b * Math.Sin(c * Math.PI / 180)),
            _ => throw new NotSupportedException($"Unsupported color space '{space}'."),
        };
    }

    /// <summary>Converts CIE XYZ D65 to components of <paramref name="space"/>. Values are not gamut-mapped.</summary>
    public static double[] FromXyzD65(string space, double[] xyz)
    {
        switch (space)
        {
            case ColorSpaces.Srgb: return Map(Multiply(s_xyzToSrgb, xyz), LinearToSrgb);
            case ColorSpaces.SrgbLinear: return Multiply(s_xyzToSrgb, xyz);
            case ColorSpaces.Hsl: { var s = FromXyzD65(ColorSpaces.Srgb, xyz); return SrgbToHsl(s[0], s[1], s[2]); }
            case ColorSpaces.Hwb: { var s = FromXyzD65(ColorSpaces.Srgb, xyz); return SrgbToHwb(s[0], s[1], s[2]); }
            case ColorSpaces.DisplayP3: return Map(Multiply(s_xyzToP3, xyz), LinearToSrgb);
            case ColorSpaces.A98Rgb: return Map(Multiply(s_xyzToA98, xyz), LinearToA98);
            case ColorSpaces.ProphotoRgb: return Map(Multiply(s_xyzD50ToProphoto, Multiply(s_d65ToD50, xyz)), LinearToProphoto);
            case ColorSpaces.Rec2020: return Map(Multiply(s_xyzToRec2020, xyz), LinearToRec2020);
            case ColorSpaces.XyzD65: return [.. xyz];
            case ColorSpaces.XyzD50: return Multiply(s_d65ToD50, xyz);
            case ColorSpaces.Lab: return XyzD50ToLab(Multiply(s_d65ToD50, xyz));
            case ColorSpaces.Lch: return ToPolar(XyzD50ToLab(Multiply(s_d65ToD50, xyz)), 0.0015);
            case ColorSpaces.Oklab: return XyzToOklab(xyz);
            case ColorSpaces.Oklch: return ToPolar(XyzToOklab(xyz), 0.000004);
            default: throw new NotSupportedException($"Unsupported color space '{space}'.");
        }
    }

    public static double[] Convert(string from, IReadOnlyList<double?> components, string to)
        => from == to ? components.Select(static c => c ?? 0).ToArray() : FromXyzD65(to, ToXyzD65(from, components));

    /// <summary>Converts to gamma-encoded sRGB without clipping (check <see cref="SrgbColor.IsInGamut"/>).</summary>
    public static SrgbColor ToSrgb(string space, IReadOnlyList<double?> components, double alpha = 1)
    {
        double[] rgb;
        if (space == ColorSpaces.Srgb) rgb = [components[0] ?? 0, components[1] ?? 0, components[2] ?? 0];
        else if (space == ColorSpaces.Hsl) rgb = HslToSrgb(components[0] ?? 0, components[1] ?? 0, components[2] ?? 0);
        else if (space == ColorSpaces.Hwb) rgb = HwbToSrgb(components[0] ?? 0, components[1] ?? 0, components[2] ?? 0);
        else rgb = FromXyzD65(ColorSpaces.Srgb, ToXyzD65(space, components));
        return new SrgbColor(rgb[0], rgb[1], rgb[2], alpha);
    }

    /// <summary>
    /// Converts to gamma-encoded sRGB and brings out-of-gamut colors into gamut with the CSS Color Module Level 4 gamut
    /// mapping algorithm (§13.2): chroma is reduced in OKLCh, keeping lightness and hue, until the clipped result is within
    /// one just-noticeable difference (ΔEOK 0.02) of the reduced color. In-gamut colors are returned unchanged; alpha is
    /// clamped to [0, 1].
    /// </summary>
    public static SrgbColor GamutMapToSrgb(string space, IReadOnlyList<double?> components, double alpha = 1)
    {
        var a = Math.Clamp(alpha, 0, 1);
        var direct = ToSrgb(space, components, a);
        if (direct.IsInGamut) return direct.Clip();
        var lch = Convert(space, components, ColorSpaces.Oklch);
        double l = lch[0], c = lch[1], h = lch[2];
        if (l >= 1) return new SrgbColor(1, 1, 1, a);
        if (l <= 0) return new SrgbColor(0, 0, 0, a);

        const double jnd = 0.02;
        const double epsilon = 0.0001;
        var clipped = ClipOklch(l, c, h);
        if (DeltaEOk(clipped, l, c, h) < jnd) return clipped with { A = a };

        double min = 0, max = c;
        var minInGamut = true;
        while (max - min > epsilon)
        {
            var chroma = (min + max) / 2;
            var candidate = OklchToSrgb(l, chroma, h);
            if (minInGamut && candidate.IsInGamut)
            {
                min = chroma;
                continue;
            }
            clipped = candidate.Clip();
            var e = DeltaEOk(clipped, l, chroma, h);
            if (e < jnd)
            {
                if (jnd - e < epsilon) break;
                minInGamut = false;
                min = chroma;
            }
            else
            {
                max = chroma;
            }
        }
        return clipped with { A = a };

        static SrgbColor OklchToSrgb(double l, double c, double h) => ToSrgb(ColorSpaces.Oklch, [l, c, h]);
        static SrgbColor ClipOklch(double l, double c, double h) => OklchToSrgb(l, c, h).Clip();
    }

    /// <summary>ΔEOK (Euclidean distance in OKLab) between an sRGB color and an OKLCh color.</summary>
    private static double DeltaEOk(SrgbColor srgb, double l, double c, double h)
    {
        var lab = Convert(ColorSpaces.Srgb, [srgb.R, srgb.G, srgb.B], ColorSpaces.Oklab);
        var radians = h * Math.PI / 180;
        var dl = lab[0] - l;
        var da = lab[1] - c * Math.Cos(radians);
        var db = lab[2] - c * Math.Sin(radians);
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    // ---- Transfer functions ------------------------------------------------------------------------------------
    public static double SrgbToLinear(double v)
    {
        var abs = Math.Abs(v);
        return abs <= 0.04045 ? v / 12.92 : Math.Sign(v) * Math.Pow((abs + 0.055) / 1.055, 2.4);
    }

    public static double LinearToSrgb(double v)
    {
        var abs = Math.Abs(v);
        return abs > 0.0031308 ? Math.Sign(v) * (1.055 * Math.Pow(abs, 1 / 2.4) - 0.055) : 12.92 * v;
    }

    private static double A98ToLinear(double v) => Math.Sign(v) * Math.Pow(Math.Abs(v), 563.0 / 256);
    private static double LinearToA98(double v) => Math.Sign(v) * Math.Pow(Math.Abs(v), 256.0 / 563);

    private static double ProphotoToLinear(double v)
    {
        const double et2 = 16.0 / 512;
        var abs = Math.Abs(v);
        return abs <= et2 ? v / 16 : Math.Sign(v) * Math.Pow(abs, 1.8);
    }

    private static double LinearToProphoto(double v)
    {
        const double et = 1.0 / 512;
        var abs = Math.Abs(v);
        return abs >= et ? Math.Sign(v) * Math.Pow(abs, 1 / 1.8) : 16 * v;
    }

    private const double Rec2020Alpha = 1.09929682680944;
    private const double Rec2020Beta = 0.018053968510807;

    private static double Rec2020ToLinear(double v)
    {
        var abs = Math.Abs(v);
        return abs < Rec2020Beta * 4.5 ? v / 4.5 : Math.Sign(v) * Math.Pow((abs + Rec2020Alpha - 1) / Rec2020Alpha, 1 / 0.45);
    }

    private static double LinearToRec2020(double v)
    {
        var abs = Math.Abs(v);
        return abs > Rec2020Beta ? Math.Sign(v) * (Rec2020Alpha * Math.Pow(abs, 0.45) - (Rec2020Alpha - 1)) : 4.5 * v;
    }

    // ---- Cylindrical sRGB models (components in DTCG units: hue degrees, others 0-100) ----------------------------
    public static double[] HslToSrgb(double h, double s, double l)
    {
        h = NormalizeHue(h);
        s /= 100;
        l /= 100;
        double F(int n)
        {
            var k = (n + h / 30) % 12;
            var a = s * Math.Min(l, 1 - l);
            return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
        }
        return [F(0), F(8), F(4)];
    }

    public static double[] SrgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (min + max) / 2;
        var d = max - min;
        double h = 0, s = 0;
        if (d != 0)
        {
            s = l is 0 or 1 ? 0 : (max - l) / Math.Min(l, 1 - l);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        if (s < 0) { h += 180; s = Math.Abs(s); }
        return [NormalizeHue(h), s * 100, l * 100];
    }

    public static double[] HwbToSrgb(double h, double w, double bl)
    {
        w /= 100;
        bl /= 100;
        if (w + bl >= 1)
        {
            var gray = w / (w + bl);
            return [gray, gray, gray];
        }
        var rgb = HslToSrgb(h, 100, 50);
        for (var i = 0; i < 3; i++) rgb[i] = rgb[i] * (1 - w - bl) + w;
        return rgb;
    }

    public static double[] SrgbToHwb(double r, double g, double b)
    {
        var hsl = SrgbToHsl(r, g, b);
        var w = Math.Min(r, Math.Min(g, b));
        var bl = 1 - Math.Max(r, Math.Max(g, b));
        return [hsl[0], w * 100, bl * 100];
    }

    // ---- Lab / OKLab -------------------------------------------------------------------------------------------
    private static double[] LabToXyzD50(double l, double a, double b)
    {
        var f1 = (l + 16) / 116;
        var f0 = a / 500 + f1;
        var f2 = f1 - b / 200;
        var x = Math.Pow(f0, 3) > Epsilon ? Math.Pow(f0, 3) : (116 * f0 - 16) / Kappa;
        var y = l > Kappa * Epsilon ? Math.Pow((l + 16) / 116, 3) : l / Kappa;
        var z = Math.Pow(f2, 3) > Epsilon ? Math.Pow(f2, 3) : (116 * f2 - 16) / Kappa;
        return [x * s_d50White[0], y * s_d50White[1], z * s_d50White[2]];
    }

    private static double[] XyzD50ToLab(double[] xyz)
    {
        var f = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var v = xyz[i] / s_d50White[i];
            f[i] = v > Epsilon ? Math.Cbrt(v) : (Kappa * v + 16) / 116;
        }
        return [116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])];
    }

    private static double[] OklabToXyz(double l, double a, double b)
    {
        var lms = Multiply(s_oklabToLms, [l, a, b]);
        return Multiply(s_lmsToXyz, [Math.Pow(lms[0], 3), Math.Pow(lms[1], 3), Math.Pow(lms[2], 3)]);
    }

    private static double[] XyzToOklab(double[] xyz)
    {
        var lms = Multiply(s_xyzToLms, xyz);
        return Multiply(s_lmsToOklab, [Math.Cbrt(lms[0]), Math.Cbrt(lms[1]), Math.Cbrt(lms[2])]);
    }

    private static double[] ToPolar(double[] lab, double achromaticThreshold)
    {
        var c = Math.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]);
        var h = c < achromaticThreshold ? 0 : NormalizeHue(Math.Atan2(lab[2], lab[1]) * 180 / Math.PI);
        return [lab[0], c, h];
    }

    public static double NormalizeHue(double h)
    {
        var r = h % 360;
        return r < 0 ? r + 360 : r;
    }

    // ---- Linear algebra ----------------------------------------------------------------------------------------
    private static double[] Multiply(double[,] m, double[] v) =>
    [
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
    ];

    private static double[] Map(double[] v, Func<double, double> f) => [f(v[0]), f(v[1]), f(v[2])];

    private static IReadOnlyList<double?> ToNullable(double[] v) => [v[0], v[1], v[2]];

    private static double[,] Invert(double[,] m)
    {
        var a = m[0, 0]; var b = m[0, 1]; var c = m[0, 2];
        var d = m[1, 0]; var e = m[1, 1]; var f = m[1, 2];
        var g = m[2, 0]; var h = m[2, 1]; var i = m[2, 2];
        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        return new[,]
        {
            { (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det },
            { (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det },
            { (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det },
        };
    }
}

/// <summary>WCAG 2.x relative luminance, contrast ratio and alpha compositing.</summary>
public static class Wcag
{
    /// <summary>Relative luminance per WCAG 2.2 (sRGB, threshold 0.04045). The input must be in gamut.</summary>
    public static double RelativeLuminance(SrgbColor c)
    {
        static double Channel(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        var clipped = c.Clip();
        return 0.2126 * Channel(clipped.R) + 0.7152 * Channel(clipped.G) + 0.0722 * Channel(clipped.B);
    }

    /// <summary>Contrast ratio between 1 and 21. Not rounded: 4.499 does not meet 4.5:1.</summary>
    public static double ContrastRatio(SrgbColor a, SrgbColor b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Source-over compositing in gamma-encoded sRGB, matching default browser behaviour.</summary>
    public static SrgbColor CompositeOver(SrgbColor top, SrgbColor bottom)
    {
        var a = Math.Clamp(top.A, 0, 1);
        var ba = Math.Clamp(bottom.A, 0, 1);
        var outA = a + ba * (1 - a);
        if (outA <= 0) return new SrgbColor(0, 0, 0, 0);
        double Mix(double t, double b) => (t * a + b * ba * (1 - a)) / outA;
        return new SrgbColor(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B), outA);
    }
}
