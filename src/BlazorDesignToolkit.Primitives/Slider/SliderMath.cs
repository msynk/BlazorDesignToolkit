namespace BlazorDesignToolkit.Primitives;

/// <summary>Value arithmetic shared by <see cref="Slider"/>, <see cref="RangeSlider"/> and <see cref="NumberField"/>.</summary>
public static class SliderMath
{
    /// <summary>Snaps to the nearest step from <paramref name="min"/> and clamps to [min, max]. Avoids floating-point drift.</summary>
    public static double Snap(double value, double min, double max, double step)
    {
        if (double.IsNaN(value)) return min;
        var clamped = Math.Clamp(value, min, max);
        if (step <= 0) return clamped;
        var steps = Math.Round((clamped - min) / step, MidpointRounding.AwayFromZero);
        var snapped = min + steps * step;
        if (snapped > max) snapped -= step;
        // Round to the step's precision so 0.1 + 0.2 shows as 0.3.
        return Math.Round(snapped, Decimals(step) + Decimals(min));
    }

    /// <summary>Value for a pointer position: <paramref name="fraction"/> 0–1 along the track.</summary>
    public static double FromFraction(double fraction, double min, double max, double step)
        => Snap(min + Math.Clamp(fraction, 0, 1) * (max - min), min, max, step);

    /// <summary>A tenth of the range, rounded to whole steps (at least one step).</summary>
    public static double DefaultLargeStep(double min, double max, double step)
        => step <= 0 ? (max - min) / 10 : Math.Max(step, Math.Round((max - min) / 10 / step, MidpointRounding.AwayFromZero) * step);

    public static double ToPercent(double value, double min, double max)
        => max <= min ? 0 : Math.Clamp((value - min) / (max - min), 0, 1) * 100;

    /// <summary>The value after a key press (APG slider keys), or null for other keys.</summary>
    public static double? KeyValue(string key, double value, double min, double max, double step, double largeStep, Orientation orientation, TextDirection direction)
    {
        var rtl = direction == TextDirection.Rtl && orientation == Orientation.Horizontal;
        double? next = key switch
        {
            "ArrowRight" => rtl ? value - step : value + step,
            "ArrowUp" => value + step,
            "ArrowLeft" => rtl ? value + step : value - step,
            "ArrowDown" => value - step,
            "PageUp" => value + largeStep,
            "PageDown" => value - largeStep,
            "Home" => min,
            "End" => max,
            _ => null,
        };
        return next is { } n ? Snap(n, min, max, step) : null;
    }

    private static int Decimals(double value)
    {
        var text = Math.Abs(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0) return Math.Max(0, -int.Parse(text[(e + 1)..], System.Globalization.CultureInfo.InvariantCulture)) + 2;
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : Math.Min(10, text.Length - dot - 1);
    }
}
