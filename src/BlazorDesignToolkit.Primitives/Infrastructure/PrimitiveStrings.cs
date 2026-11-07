using System.Globalization;

namespace BlazorDesignToolkit.Primitives;

/// <summary>
/// Built-in texts of the primitives: accessible names, announcements and empty states (English). To localize, derive from
/// this class and override the members (for example from an <c>IStringLocalizer</c> or resource files, reading
/// <see cref="CultureInfo.CurrentUICulture"/>), then register it:
/// <c>services.AddHeadlessPrimitives().AddSingleton&lt;PrimitiveStrings, MyStrings&gt;()</c> (or scoped, to follow a per-user
/// culture). Component parameters such as <c>Label</c> still take precedence for a single instance.
/// </summary>
public class PrimitiveStrings
{
    public static PrimitiveStrings Default { get; } = new();

    // Combobox
    public virtual string ComboboxNoResults => "No results";
    public virtual string ComboboxResults(int count) => count == 1 ? "1 result available." : $"{count} results available.";

    // Command menu
    public virtual string CommandMenuLabel => "Command menu";
    public virtual string CommandMenuPlaceholder => "Type a command or search…";
    public virtual string CommandMenuEmpty => "No matching commands";
    public virtual string CommandMenuResults(int count) => count == 1 ? "1 command available." : $"{count} commands available.";

    // Toasts
    public virtual string ToastRegionLabel => "Notifications";
    public virtual string ToastDismiss => "Dismiss notification";

    // Range slider thumbs
    public virtual string SliderMinimum => "Minimum";
    public virtual string SliderMaximum => "Maximum";

    // Number field
    public virtual string NumberFieldIncrement => "Increase";
    public virtual string NumberFieldDecrement => "Decrease";

    // Date picker
    public virtual string DatePickerOpen => "Choose date";
    public virtual string DatePickerDialogLabel => "Choose date";
    public virtual string DatePickerPreviousMonth => "Previous month";
    public virtual string DatePickerNextMonth => "Next month";
    public virtual string DatePickerPreviousYear => "Previous year";
    public virtual string DatePickerNextYear => "Next year";
    public virtual string DatePickerToday => "Today";
    public virtual string DatePickerSelected(DateOnly date, CultureInfo culture) => $"Selected date: {date.ToString("D", culture)}";
    public virtual string DatePickerKeyboardHelp => "Use arrow keys to move between days, Page Up and Page Down to change the month, Enter to choose.";

    // Lists
    public virtual string Loading => "Loading…";
}
