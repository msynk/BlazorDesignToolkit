namespace BlazorDesignToolkit.Primitives;

/// <summary>An item registered with a composite widget (tab, menu item, accordion trigger…).</summary>
public interface ICompositeItem
{
    string ElementId { get; }
    bool IsDisabled { get; }
    /// <summary>Text used for type-to-select.</summary>
    string? TypeaheadLabel { get; }
}

/// <summary>
/// Ordered registry of the items of a composite widget. Items register in initialization order, which differs from
/// document order when items are nested in other components or added later, so the order is re-synchronized with the
/// DOM (one interop call) before the first keyboard move after any registration.
/// </summary>
public sealed class CompositeRegistry<TItem> where TItem : class, ICompositeItem
{
    private readonly List<TItem> _items = [];

    public IReadOnlyList<TItem> Items => _items;

    /// <summary>The item that currently has DOM focus (set on focus, cleared on blur).</summary>
    public TItem? Focused { get; private set; }

    public void OnFocus(TItem item) => Focused = item;

    public void OnBlur(TItem item)
    {
        if (ReferenceEquals(Focused, item)) Focused = null;
    }

    /// <summary>True when the order may differ from the DOM order.</summary>
    public bool OrderDirty { get; private set; }

    internal bool Rendered { get; set; }

    public void Register(TItem item)
    {
        if (_items.Contains(item)) return;
        _items.Add(item);
        OrderDirty = true;
    }

    public void Unregister(TItem item)
    {
        _items.Remove(item);
        if (ReferenceEquals(Focused, item)) Focused = null;
    }

    public int IndexOf(TItem? item) => item is null ? -1 : _items.IndexOf(item);

    public TItem? FindById(string? id) => id is null ? null : _items.Find(i => i.ElementId == id);

    /// <summary>Reorders items to match <paramref name="domOrder"/> (element ids in document order).</summary>
    public void ApplyOrder(IReadOnlyList<string>? domOrder)
    {
        OrderDirty = false;
        if (domOrder is null || domOrder.Count == 0) return;
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < domOrder.Count; i++) position[domOrder[i]] = i;
        var sorted = _items.OrderBy(i => position.TryGetValue(i.ElementId, out var p) ? p : int.MaxValue).ToList();
        _items.Clear();
        _items.AddRange(sorted);
    }

    /// <summary>Applies keyboard navigation and returns the target item (or the current one when nothing changes).</summary>
    public TItem? Navigate(TItem? current, NavigationIntent intent, bool loop)
    {
        var index = KeyboardNavigation.Move(IndexOf(current), _items.Count, i => _items[i].IsDisabled, intent, loop);
        return index >= 0 ? _items[index] : null;
    }

    public TItem? Typeahead(Typeahead typeahead, char key, TItem? current)
    {
        var labels = _items.Select(static i => i.TypeaheadLabel ?? "").ToList();
        var index = typeahead.Search(key, IndexOf(current), labels, i => _items[i].IsDisabled || string.IsNullOrEmpty(labels[i]));
        return index >= 0 && index < _items.Count ? _items[index] : current;
    }
}
