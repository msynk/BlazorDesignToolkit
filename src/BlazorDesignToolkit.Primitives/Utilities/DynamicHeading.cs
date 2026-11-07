using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>Renders &lt;h1&gt;…&lt;h6&gt; for the given level, so composite widgets fit the page's heading outline.</summary>
public sealed class DynamicHeading : ComponentBase
{
    [Parameter] public int Level { get; set; } = 2;
    [Parameter] public IReadOnlyDictionary<string, object>? Attributes { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Level is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(Level), "Heading level must be between 1 and 6.");
        builder.OpenElement(0, "h" + Level);
        builder.AddMultipleAttributes(1, Attributes);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
