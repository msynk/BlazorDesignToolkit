using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>The described control: a &lt;button&gt; carrying aria-describedby. Its content must provide the accessible name.</summary>
public sealed class TooltipTrigger : PrimitiveBase
{
    [CascadingParameter] internal Tooltip.TooltipContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Context is null) throw new InvalidOperationException("TooltipTrigger must be placed inside Tooltip.");
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddMultipleAttributes(1, Attributes("type", "aria-describedby"));
        builder.AddAttribute(2, "type", "button");
        var describedBy = AdditionalAttributes?.TryGetValue("aria-describedby", out var existing) == true ? $"{existing} {Context.TooltipId}" : Context.TooltipId;
        builder.AddAttribute(3, "aria-describedby", describedBy);
        builder.AddElementReferenceCapture(4, r =>
        {
            Context.Trigger = r;
            Context.TriggerRendered = true;
        });
        builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>The tooltip text (role="tooltip"). Always rendered so that aria-describedby resolves; hidden until shown.</summary>
public sealed class TooltipContent : PrimitiveBase
{
    private ElementReference _element;
    private bool _attached;

    [CascadingParameter] internal Tooltip.TooltipContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, Attributes("id", "role", "popover"));
        builder.AddAttribute(2, "id", Context.TooltipId);
        builder.AddAttribute(3, "role", "tooltip");
        builder.AddAttribute(4, "popover", "manual");
        builder.AddAttribute(5, "data-state", "closed");
        builder.AddElementReferenceCapture(6, r => _element = r);
        builder.AddContent(7, ChildContent);
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_attached || !Context.TriggerRendered) return;
        _attached = true;
        await Js.TrackAsync("attachTooltip", Context.Trigger, _element, new
        {
            showDelay = Context.Owner.ShowDelay,
            hideDelay = Context.Owner.HideDelay,
            placement = Context.Owner.Placement,
        });
    }
}
