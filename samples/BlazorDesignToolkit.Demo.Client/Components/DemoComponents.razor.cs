using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorDesignToolkit.Demo.Client.Components;

/// <summary>A titled section of a demo page.</summary>
public sealed class Example : ComponentBase
{
    private static int s_next;
    private readonly string _id = "example-" + Interlocked.Increment(ref s_next);

    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? Description { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Code { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "example");
        builder.AddAttribute(2, "aria-labelledby", _id);
        builder.OpenElement(3, "h2");
        builder.AddAttribute(4, "id", _id);
        builder.AddContent(5, Title);
        builder.CloseElement();
        if (Description is not null)
        {
            builder.OpenElement(6, "div");
            builder.AddAttribute(7, "class", "example-description");
            builder.AddContent(8, Description);
            builder.CloseElement();
        }
        builder.OpenElement(9, "div");
        builder.AddAttribute(10, "class", "example-stage");
        builder.AddContent(11, ChildContent);
        builder.CloseElement();
        if (Code is not null)
        {
            builder.OpenComponent<CodeBlock>(12);
            builder.AddAttribute(13, nameof(CodeBlock.Code), Code);
            builder.CloseComponent();
        }
        builder.CloseElement();
    }
}

/// <summary>Code sample with a copy button.</summary>
public sealed class CodeBlock : ComponentBase
{
    private string _status = "";

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Parameter, EditorRequired] public string Code { get; set; } = "";
    [Parameter] public string? Label { get; set; }

    private async Task CopyAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", Code);
            _status = "Copied";
        }
        catch (JSException)
        {
            _status = "Copy failed; select the text manually";
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "code-block");
        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "type", "button");
        builder.AddAttribute(4, "class", "copy");
        builder.AddAttribute(5, "onclick", EventCallback.Factory.Create(this, CopyAsync));
        builder.AddContent(6, Label is null ? "Copy code" : $"Copy {Label}");
        builder.CloseElement();
        builder.OpenElement(7, "span");
        builder.AddAttribute(8, "role", "status");
        builder.AddAttribute(9, "class", "copy-status");
        builder.AddContent(10, _status);
        builder.CloseElement();
        builder.OpenElement(11, "pre");
        builder.AddAttribute(12, "tabindex", "0");
        builder.AddAttribute(13, "aria-label", Label ?? "Code sample");
        builder.OpenElement(14, "code");
        builder.AddContent(15, Code);
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    }
}

/// <summary>Keyboard interaction table for a primitive.</summary>
public sealed class KeyboardTable : ComponentBase
{
    [Parameter, EditorRequired] public IReadOnlyList<(string Keys, string Behavior)> Rows { get; set; } = [];
    [Parameter] public string Caption { get; set; } = "Keyboard interaction";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var seq = 0;
        builder.OpenElement(seq++, "table");
        builder.AddAttribute(seq++, "class", "keyboard-table");
        builder.OpenElement(seq++, "caption");
        builder.AddContent(seq++, Caption);
        builder.CloseElement();
        builder.OpenElement(seq++, "thead");
        builder.OpenElement(seq++, "tr");
        builder.OpenElement(seq++, "th");
        builder.AddAttribute(seq++, "scope", "col");
        builder.AddContent(seq++, "Key");
        builder.CloseElement();
        builder.OpenElement(seq++, "th");
        builder.AddAttribute(seq++, "scope", "col");
        builder.AddContent(seq++, "Behavior");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
        builder.OpenElement(seq++, "tbody");
        foreach (var (keys, behavior) in Rows)
        {
            builder.OpenElement(seq, "tr");
            builder.OpenElement(seq + 1, "th");
            builder.AddAttribute(seq + 2, "scope", "row");
            builder.OpenElement(seq + 3, "kbd");
            builder.AddContent(seq + 4, keys);
            builder.CloseElement();
            builder.CloseElement();
            builder.OpenElement(seq + 5, "td");
            builder.AddContent(seq + 6, behavior);
            builder.CloseElement();
            builder.CloseElement();
        }
        builder.CloseElement();
        builder.CloseElement();
    }
}
