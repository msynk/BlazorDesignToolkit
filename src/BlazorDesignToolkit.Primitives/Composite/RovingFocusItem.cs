using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>A focusable item in a <see cref="RovingFocusGroup"/>. Renders a &lt;button&gt; by default.</summary>
public sealed class RovingFocusItem : PrimitiveBase, ICompositeItem
{
    private ElementReference _element;
    private string _id = "";

    [CascadingParameter] internal RovingFocusGroup.RovingContext Context { get; set; } = default!;
    [Parameter] public string Element { get; set; } = "button";
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    public string ElementId => _id;
    public bool IsDisabled => Disabled;
    public string? TypeaheadLabel => null;

    protected override void OnInitialized()
    {
        if (Context is null) throw new InvalidOperationException("RovingFocusItem must be placed inside RovingFocusGroup.");
        _id = ProvidedId ?? Ids.Next("roving");
        Context.Items.Register(this);
        Track(Context);
    }

    internal ValueTask FocusAsync() => _element.FocusAsync();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Element);
        builder.AddMultipleAttributes(1, Attributes("tabindex", "id"));
        builder.AddAttribute(2, "id", _id);
        if (Element == "button") builder.AddAttribute(3, "type", "button");
        builder.AddAttribute(4, "tabindex", Context.TabStop == this ? "0" : "-1");
        builder.AddAttribute(5, "data-roving-item", "");
        if (Disabled)
        {
            builder.AddAttribute(6, "aria-disabled", "true");
            builder.AddAttribute(7, "data-disabled", "");
        }
        builder.AddAttribute(8, "onfocus", EventCallback.Factory.Create(this, () =>
        {
            Context.Items.OnFocus(this);
            if (Context.Current != this)
            {
                Context.Current = this;
                Context.NotifyStateChanged();
            }
        }));
        builder.AddAttribute(9, "onblur", EventCallback.Factory.Create(this, () => Context.Items.OnBlur(this)));
        builder.AddElementReferenceCapture(10, r => _element = r);
        builder.AddContent(11, ChildContent);
        builder.CloseElement();
    }

    public override ValueTask DisposeAsync()
    {
        Context.Items.Unregister(this);
        if (Context.Current == this) Context.Current = null;
        return base.DisposeAsync();
    }
}
