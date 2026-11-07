using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>Button that opens the dialog (aria-haspopup="dialog", aria-expanded, aria-controls).</summary>
public sealed class DialogTrigger : PrimitiveBase
{
    [CascadingParameter] internal Dialog.DialogContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Context is null) throw new InvalidOperationException("DialogTrigger must be placed inside Dialog.");
        Track(Context);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var open = Context.Owner.IsOpen;
        builder.OpenElement(0, "button");
        builder.AddMultipleAttributes(1, Attributes("aria-haspopup", "aria-expanded", "aria-controls", "type"));
        builder.AddAttribute(2, "type", "button");
        builder.AddAttribute(3, "aria-haspopup", "dialog");
        builder.AddAttribute(4, "aria-expanded", open ? "true" : "false");
        builder.AddAttribute(5, "aria-controls", Context.ContentId);
        builder.AddAttribute(6, "data-state", open ? "open" : "closed");
        builder.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, () => Context.Owner.SetOpenAsync(true)));
        builder.AddContent(8, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>Dialog heading; becomes the dialog's accessible name (aria-labelledby).</summary>
public sealed class DialogTitle : PrimitiveBase
{
    [CascadingParameter] internal Dialog.DialogContext Context { get; set; } = default!;
    [Parameter] public int Level { get; set; } = 2;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        Context.HasTitle = true;
        Context.NotifyStateChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DynamicHeading>(0);
        builder.AddAttribute(1, nameof(DynamicHeading.Level), Level);
        builder.AddAttribute(2, nameof(DynamicHeading.Attributes), Merge(AdditionalAttributes, "id", Context.TitleId));
        builder.AddAttribute(3, nameof(DynamicHeading.ChildContent), ChildContent);
        builder.CloseComponent();
    }

    public override ValueTask DisposeAsync()
    {
        Context.HasTitle = false;
        Context.NotifyStateChanged();
        return base.DisposeAsync();
    }

    internal static IReadOnlyDictionary<string, object> Merge(IReadOnlyDictionary<string, object>? attributes, string key, object value)
    {
        var result = attributes is null ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, object>(attributes, StringComparer.OrdinalIgnoreCase);
        result[key] = value;
        return result;
    }
}

/// <summary>Dialog description (aria-describedby).</summary>
public sealed class DialogDescription : PrimitiveBase
{
    [CascadingParameter] internal Dialog.DialogContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        Context.HasDescription = true;
        Context.NotifyStateChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "id", Context.DescriptionId);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }

    public override ValueTask DisposeAsync()
    {
        Context.HasDescription = false;
        Context.NotifyStateChanged();
        return base.DisposeAsync();
    }
}

/// <summary>Button that closes the dialog. Give icon-only close buttons an aria-label.</summary>
public sealed class DialogClose : PrimitiveBase
{
    [CascadingParameter] internal Dialog.DialogContext Context { get; set; } = default!;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    /// <summary>Invoked before closing (for example to save).</summary>
    [Parameter] public EventCallback OnClick { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddMultipleAttributes(1, Attributes("type"));
        builder.AddAttribute(2, "type", "button");
        builder.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, async () =>
        {
            if (OnClick.HasDelegate) await OnClick.InvokeAsync();
            await Context.Owner.SetOpenAsync(false);
        }));
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
