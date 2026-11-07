using System.Globalization;

namespace BlazorDesignToolkit.Primitives;

/// <summary>Calendar arithmetic for <see cref="DatePicker"/> (Gregorian calendar, culture-specific first day of week).</summary>
public static class CalendarMath
{
    /// <summary>The 42 days (6 weeks) shown for <paramref name="month"/>, starting on the culture's first day of the week.</summary>
    public static IReadOnlyList<DateOnly> MonthGrid(DateOnly month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    /// <summary>Weekdays in display order.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDays(DayOfWeek firstDayOfWeek)
        => Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)firstDayOfWeek + i) % 7)).ToList();

    /// <summary>The date after a grid key (APG date picker), or null for other keys.</summary>
    public static DateOnly? Move(string key, bool shift, DateOnly focused, DayOfWeek firstDayOfWeek, bool rtl = false)
    {
        var startOfWeek = focused.AddDays(-(((int)focused.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
        return key switch
        {
            "ArrowRight" => focused.AddDays(rtl ? -1 : 1),
            "ArrowLeft" => focused.AddDays(rtl ? 1 : -1),
            "ArrowDown" => focused.AddDays(7),
            "ArrowUp" => focused.AddDays(-7),
            "Home" => startOfWeek,
            "End" => startOfWeek.AddDays(6),
            "PageUp" => shift ? focused.AddYears(-1) : focused.AddMonths(-1),
            "PageDown" => shift ? focused.AddYears(1) : focused.AddMonths(1),
            _ => null,
        };
    }

    public static DateOnly Clamp(DateOnly date, DateOnly? min, DateOnly? max)
    {
        if (min is { } lo && date < lo) return lo;
        if (max is { } hi && date > hi) return hi;
        return date;
    }

    /// <summary>Parses user text with the culture (short date pattern first, then any culture format). Null if not a date.</summary>
    public static DateOnly? Parse(string text, CultureInfo culture)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        if (DateOnly.TryParseExact(trimmed, culture.DateTimeFormat.ShortDatePattern, culture, DateTimeStyles.AllowWhiteSpaces, out var exact)) return exact;
        return DateOnly.TryParse(trimmed, culture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
    }
}
