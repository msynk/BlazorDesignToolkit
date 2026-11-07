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
        return RoundToStep(snapped, min, step);
    }

    /// <summary>
    /// Like <see cref="Snap"/>, and also keeps the result within [<paramref name="lower"/>, <paramref name="upper"/>] (for
    /// example a thumb limited by the other thumb): a snapped value outside moves to the nearest step inside, or to the bound
    /// itself when no step lies inside. Null when the bounds leave no room inside [min, max].
    /// </summary>
    public static double? SnapWithin(double value, double min, double max, double step, double lower, double upper)
    {
        lower = Math.Max(lower, min);
        upper = Math.Min(upper, max);
        if (double.IsNaN(lower) || double.IsNaN(upper) || upper < lower) return null;
        var snapped = Snap(value, min, max, step);
        if (snapped > upper)
        {
            snapped = step > 0 ? StepToward(upper, min, step, down: true) : upper;
            if (snapped < lower || snapped > upper) snapped = upper;
        }
        else if (snapped < lower)
        {
            snapped = step > 0 ? StepToward(lower, min, step, down: false) : lower;
            if (snapped < lower || snapped > upper) snapped = lower;
        }
        return snapped;
    }

    /// <summary>The step value (from <paramref name="min"/>) nearest to <paramref name="bound"/> on the inner side: at or below it, or at or above it.</summary>
    private static double StepToward(double bound, double min, double step, bool down)
    {
        // Tolerate floating-point noise so that a bound that is a step value is not moved a whole step.
        var steps = (bound - min) / step;
        steps = down ? Math.Floor(steps + 1e-9) : Math.Ceiling(steps - 1e-9);
        return RoundToStep(min + steps * step, min, step);
    }

    /// <summary>Rounds to the step's precision so that 0.1 + 0.2 shows as 0.3 (Math.Round accepts at most 15 digits).</summary>
    private static double RoundToStep(double value, double min, double step)
        => Math.Round(value, Math.Min(15, Decimals(step) + Decimals(min)));

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
