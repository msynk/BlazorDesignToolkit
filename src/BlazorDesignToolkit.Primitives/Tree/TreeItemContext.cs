namespace BlazorDesignToolkit.Primitives;

/// <summary>Context passed to tree item templates.</summary>
public sealed record TreeItemContext<TItem>(TItem Item, int Level, string Label, bool HasChildren, bool Expanded, bool Selected, bool Disabled);
