using Microsoft.AspNetCore.Components.Web;

namespace BlazorDesignToolkit.Primitives;

/// <summary>Context passed to option templates.</summary>
public sealed record OptionContext<TItem>(TItem Item, int Index, string Label, bool Selected, bool Active, bool Disabled);

/// <summary>
/// Shared option-list state for Listbox, Select, Combobox and CommandMenu: active (highlighted) option, keyboard moves and
/// type-to-select. Pure logic, independent of rendering, so it is unit-testable.
/// </summary>
public sealed class OptionList<TItem>
{
    private readonly Typeahead _typeahead;

    public OptionList(TimeProvider? time = null) => _typeahead = new Typeahead(time);

    public IReadOnlyList<TItem> Items { get; private set; } = [];
    public Func<TItem, string> Label { get; private set; } = static i => i?.ToString() ?? "";
    public Func<TItem, bool> Disabled { get; private set; } = static _ => false;
    public int ActiveIndex { get; set; } = -1;

    public void Update(IEnumerable<TItem>? items, Func<TItem, string>? label, Func<TItem, bool>? disabled)
    {
        var previous = ActiveIndex >= 0 && ActiveIndex < Items.Count ? Items[ActiveIndex] : default;
        Items = items?.ToList() ?? [];
        Label = label ?? (static i => i?.ToString() ?? "");
        Disabled = disabled ?? (static _ => false);
        // Keep the active item when the list changes (for example while filtering), otherwise clear it.
        ActiveIndex = previous is null ? -1 : IndexOf(previous);
        if (ActiveIndex >= Items.Count) ActiveIndex = -1;
    }

    public int IndexOf(TItem item) => Items.ToList().FindIndex(i => EqualityComparer<TItem>.Default.Equals(i, item));

    public bool IsDisabled(int index) => index < 0 || index >= Items.Count || Disabled(Items[index]);

    public TItem? Active => ActiveIndex >= 0 && ActiveIndex < Items.Count ? Items[ActiveIndex] : default;

    public bool HasActive => ActiveIndex >= 0 && ActiveIndex < Items.Count;

    /// <summary>Moves the active option; returns true when it changed.</summary>
    public bool Move(NavigationIntent intent, bool loop)
    {
        var next = KeyboardNavigation.Move(ActiveIndex, Items.Count, IsDisabled, intent, loop);
        if (next == ActiveIndex) return false;
        ActiveIndex = next;
        return true;
    }

    /// <summary>Activates the first enabled option matching typed characters.</summary>
    public bool Type(char key)
    {
        var labels = Items.Select(Label).ToList();
        var next = _typeahead.Search(key, ActiveIndex, labels, IsDisabled);
        if (next == ActiveIndex || next < 0) return false;
        ActiveIndex = next;
        return true;
    }

    /// <summary>Activates <paramref name="item"/> when present and enabled, otherwise the first enabled option.</summary>
    public void ActivateOrFirst(TItem? item, bool last = false)
    {
        var index = item is null ? -1 : IndexOf(item);
        ActiveIndex = index >= 0 && !IsDisabled(index) ? index : KeyboardNavigation.Move(-1, Items.Count, IsDisabled, last ? NavigationIntent.Last : NavigationIntent.First);
    }

    public static bool IsTypeaheadKey(KeyboardEventArgs e) => KeyboardNavigation.IsPrintable(e) && e.Key != " ";
}
