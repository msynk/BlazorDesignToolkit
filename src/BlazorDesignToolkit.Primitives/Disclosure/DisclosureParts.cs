using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>The button that toggles a <see cref="Disclosure"/>.</summary>
public sealed class DisclosureTrigger : PrimitiveBase
{
    [CascadingParameter] internal Disclosure.DisclosureContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Context is null) throw new InvalidOperationException("DisclosureTrigger must be placed inside Disclosure.");
        Track(Context);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var open = Context.Owner.IsOpen;
        builder.OpenElement(0, "button");
        builder.AddMultipleAttributes(1, Attributes("aria-expanded", "aria-controls", "type"));
        builder.AddAttribute(2, "type", "button");
        builder.AddAttribute(3, "id", ProvidedId ?? Context.TriggerId);
        builder.AddAttribute(4, "aria-expanded", open ? "true" : "false");
        builder.AddAttribute(5, "aria-controls", Context.PanelId);
        builder.AddAttribute(6, "disabled", Context.Owner.Disabled);
        builder.AddAttribute(7, "data-state", open ? "open" : "closed");
        builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this, Context.Owner.ToggleAsync));
        builder.AddContent(9, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>The region shown by a <see cref="Disclosure"/>. Hidden content is removed unless <see cref="KeepMounted"/> is set.</summary>
public sealed class DisclosurePanel : PrimitiveBase
{
    [CascadingParameter] internal Disclosure.DisclosureContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    /// <summary>Keep the content in the DOM with the hidden attribute (preserves form state; still hidden from assistive technology).</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override void OnInitialized() => Track(Context);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var open = Context.Owner.IsOpen;
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, Attributes("id", "hidden"));
        builder.AddAttribute(2, "id", Context.PanelId);
        builder.AddAttribute(3, "hidden", !open);
        builder.AddAttribute(4, "data-state", open ? "open" : "closed");
        if (open || KeepMounted) builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }
}
