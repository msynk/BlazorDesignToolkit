namespace BlazorDesignToolkit.Primitives;

/// <summary>A command shown in <see cref="CommandMenu"/>.</summary>
public sealed record CommandItem(string Id, string Label, Func<Task> Execute)
{
    public string? Group { get; init; }
    /// <summary>Additional search terms.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Displayed shortcut hint (presentation only; hidden from assistive technology).</summary>
    public string? Shortcut { get; init; }
    public bool Disabled { get; init; }
}
