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
    /// <summary>Adds a "Live markup" panel that shows the example's rendered HTML, with its ARIA, as it changes.</summary>
    [Parameter] public bool Inspect { get; set; }

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
        if (ChildContent is not null)
        {
            builder.OpenElement(9, "div");
            builder.AddAttribute(10, "class", "example-stage");
            builder.AddAttribute(11, "id", _id + "-stage");
            builder.AddContent(12, ChildContent);
            builder.CloseElement();
            if (Inspect)
            {
                builder.OpenComponent<LiveMarkup>(13);
                builder.AddAttribute(14, nameof(LiveMarkup.TargetId), _id + "-stage");
                builder.CloseComponent();
            }
        }
        if (Code is not null)
        {
            builder.OpenComponent<CodeBlock>(15);
            builder.AddAttribute(16, nameof(CodeBlock.Code), Code);
            builder.CloseComponent();
        }
        builder.CloseElement();
    }
}

/// <summary>Code sample with a copy button.</summary>
public sealed class CodeBlock : ComponentBase, IDisposable
{
    private string _status = "";
    private CancellationTokenSource? _clearStatus;

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
            // The Clipboard API is unavailable on insecure origins and when the permission is denied.
            _status = "Copy failed; select the text manually";
        }

        // Clear the status after a moment so that a later copy is announced again.
        _clearStatus?.Cancel();
        _clearStatus = new CancellationTokenSource();
        var token = _clearStatus.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(_ => InvokeAsync(() =>
        {
            _status = "";
            StateHasChanged();
        }), token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "code-block");
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "code-toolbar");
        builder.OpenElement(4, "span");
        builder.AddAttribute(5, "role", "status");
        builder.AddAttribute(6, "class", "copy-status");
        builder.AddContent(7, _status);
        builder.CloseElement();
        builder.OpenElement(8, "button");
        builder.AddAttribute(9, "type", "button");
        builder.AddAttribute(10, "class", "copy");
        builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, CopyAsync));
        builder.AddContent(12, Label is null ? "Copy code" : $"Copy {Label}");
        builder.CloseElement();
        builder.CloseElement();
        builder.OpenElement(13, "pre");
        builder.AddAttribute(14, "tabindex", "0");
        builder.AddAttribute(15, "aria-label", Label ?? "Code sample");
        builder.OpenElement(16, "code");
        builder.AddContent(17, Code);
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    }

    public void Dispose()
    {
        _clearStatus?.Cancel();
        _clearStatus?.Dispose();
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
