using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDesignToolkit.Primitives;

/// <summary>&lt;label&gt; associated with the field's control.</summary>
public sealed class FormLabel : PrimitiveBase
{
    [CascadingParameter] private FormFieldContext? Field { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized() => TrackField(Field);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        if (Field is not null)
        {
            builder.AddAttribute(2, "id", Field.LabelId);
            if (Field.LabelTargetsControl) builder.AddAttribute(3, "for", Field.ControlId);
        }
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}

/// <summary>Help text referenced by the control through aria-describedby.</summary>
public sealed class FormDescription : PrimitiveBase
{
    [CascadingParameter] private FormFieldContext? Field { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Field is not null)
        {
            Field.HasDescription = true;
            Field.NotifyChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        if (Field is not null) builder.AddAttribute(2, "id", Field.DescriptionId);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }

    public override ValueTask DisposeAsync()
    {
        if (Field is not null)
        {
            Field.HasDescription = false;
            Field.NotifyChanged();
        }
        return base.DisposeAsync();
    }
}

/// <summary>
/// Error message. Rendered only while the field is invalid; referenced by aria-describedby and aria-errormessage.
/// It is not a live region by default (screen readers announce it when focus returns to the control);
/// set <see cref="Announce"/> to announce changes politely.
/// </summary>
public sealed class FormError : PrimitiveBase
{
    [CascadingParameter] private FormFieldContext? Field { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public bool Announce { get; set; }

    protected override void OnInitialized()
    {
        if (Field is not null)
        {
            Field.HasError = true;
            Field.NotifyChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Field is { Invalid: false }) return;
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        if (Field is not null) builder.AddAttribute(2, "id", Field.ErrorId);
        if (Announce) builder.AddAttribute(3, "aria-live", "polite");
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }

    public override ValueTask DisposeAsync()
    {
        if (Field is not null)
        {
            Field.HasError = false;
            Field.NotifyChanged();
        }
        return base.DisposeAsync();
    }
}

/// <summary>
/// A text input wired to the surrounding <see cref="FormField"/>. Prefer native inputs: this is a thin wrapper that adds the
/// relationships; bind with <c>@bind-Value</c>.
/// </summary>
public sealed class FieldInput : PrimitiveBase
{
    [CascadingParameter] private FormFieldContext? Field { get; set; }
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public string Type { get; set; } = "text";

    protected override void OnInitialized() => TrackField(Field);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", Type);
        if (Field is not null) builder.AddMultipleAttributes(3, Field.ControlAttributes);
        builder.AddAttribute(4, "value", Value);
        builder.AddAttribute(5, "oninput", EventCallback.Factory.CreateBinder<string?>(this, v => ValueChanged.InvokeAsync(v), Value));
        builder.CloseElement();
    }
}
