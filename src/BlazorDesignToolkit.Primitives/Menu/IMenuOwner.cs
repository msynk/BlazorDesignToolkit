namespace BlazorDesignToolkit.Primitives;

/// <summary>A root menu (<see cref="Menu"/>) or a submenu (<see cref="MenuSub"/>).</summary>
internal interface IMenuOwner
{
    bool IsOpen { get; }
    string Placement { get; }
    /// <summary>Opens the menu; <paramref name="focus"/> null keeps focus where it is (pointer hover).</summary>
    Task OpenAsync(Menu.MenuFocus? focus);
    Task CloseAsync(bool dismissed = false);
    /// <summary>Closes the whole menu chain up to the root (after an item is activated, or on Tab).</summary>
    Task CloseAllAsync();
}
