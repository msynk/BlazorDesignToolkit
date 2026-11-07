using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDesignToolkit.Primitives;

/// <summary>Common behavior of menu items: registration, roving focus, Enter/Space activation, disabled handling.</summary>
public abstract class MenuItemBase : PrimitiveBase, ICompositeItem
{
    private string _id = "";

    protected ElementReference Element { get; private set; }

    [CascadingParameter] internal Menu.MenuContext Context { get; set; } = default!;
    [Parameter] public bool Disabled { get; set; }
    /// <summary>Text used for type-to-select (defaults to nothing; set it when the content is not plain text).</summary>
    [Parameter] public string? TextValue { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    public string ElementId => _id;
    public bool IsDisabled => Disabled;
    public string? TypeaheadLabel => TextValue;

    protected abstract string Role { get; }
    protected virtual bool? Checked => null;
    /// <summary>Whether activating the item closes the menu.</summary>
    protected virtual bool ClosesMenu => true;

    protected override void OnInitialized()
    {
        if (Context is null) throw new InvalidOperationException($"{GetType().Name} must be placed inside MenuContent.");
        _id = ProvidedId ?? Ids.Next("menu-item");
        Context.Items.Register(this);
        Track(Context);
    }

    internal ValueTask FocusAsync() => Element.FocusAsync();

    protected abstract Task OnActivateAsync();

    /// <summary>Extra attributes of the item element (for example aria-haspopup on submenu triggers).</summary>
    protected virtual IEnumerable<KeyValuePair<string, object>>? ExtraAttributes => null;

    protected virtual async Task ActivateAsync()
    {
        if (Disabled) return;
        await OnActivateAsync();
        if (ClosesMenu) await Context.Owner.CloseAllAsync();
    }

    protected virtual Task OnKeyDownAsync(KeyboardEventArgs e) => e.Key is "Enter" or " " ? ActivateAsync() : Task.CompletedTask;

    /// <summary>Pointer hover: moving to another item closes a sibling submenu.</summary>
    protected virtual Task OnPointerEnterAsync() => Context.OpenSub is { } sub ? sub.CloseAsync() : Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var active = Context.Active == this;
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, Attributes("role", "tabindex", "id", "aria-checked", "aria-disabled"));
        builder.AddAttribute(2, "role", Role);
        builder.AddAttribute(3, "id", _id);
        builder.AddAttribute(4, "tabindex", "-1");
        builder.AddAttribute(5, "data-menu-item", "");
        if (Checked is { } c)
        {
            builder.AddAttribute(6, "aria-checked", c ? "true" : "false");
            builder.AddAttribute(7, "data-state", c ? "checked" : "unchecked");
        }
        if (Disabled)
        {
            builder.AddAttribute(8, "aria-disabled", "true");
            builder.AddAttribute(9, "data-disabled", "");
        }
        if (active) builder.AddAttribute(10, "data-highlighted", "");
        builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, ActivateAsync));
        builder.AddAttribute(12, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
        builder.AddAttribute(13, "onfocus", EventCallback.Factory.Create(this, () => Context.Active = this));
        builder.AddAttribute(14, "onpointerenter", EventCallback.Factory.Create(this, OnPointerEnterAsync));
        builder.AddMultipleAttributes(15, ExtraAttributes);
        builder.AddElementReferenceCapture(16, r => Element = r);
        builder.AddContent(17, ChildContent);
        builder.CloseElement();
    }

    public override ValueTask DisposeAsync()
    {
        Context.Items.Unregister(this);
        if (Context.Active == this) Context.Active = null;
        return base.DisposeAsync();
    }
}

/// <summary>role="menuitem". Activation closes the menu and returns focus to the trigger.</summary>
public sealed class MenuItem : MenuItemBase
{
    [Parameter] public EventCallback OnSelect { get; set; }
    /// <summary>Keep the menu open after activation.</summary>
    [Parameter] public bool KeepOpen { get; set; }

    protected override string Role => "menuitem";
    protected override bool ClosesMenu => !KeepOpen;
    protected override Task OnActivateAsync() => OnSelect.InvokeAsync();
}

/// <summary>role="menuitemcheckbox" with aria-checked. Keeps the menu open by default.</summary>
public sealed class MenuCheckboxItem : MenuItemBase
{
    [Parameter] public bool IsChecked { get; set; }
    [Parameter] public EventCallback<bool> IsCheckedChanged { get; set; }
    [Parameter] public bool CloseOnSelect { get; set; }

    protected override string Role => "menuitemcheckbox";
    protected override bool? Checked => IsChecked;
    protected override bool ClosesMenu => CloseOnSelect;

    protected override async Task OnActivateAsync()
    {
        IsChecked = !IsChecked;
        await IsCheckedChanged.InvokeAsync(IsChecked);
    }
}

/// <summary>role="group" of menuitemradio items with a single checked value. Label it with aria-label.</summary>
public sealed class MenuRadioGroup : PrimitiveBase
{
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    internal async Task SelectAsync(string value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, Attributes("role"));
        builder.AddAttribute(2, "role", "group");
        builder.OpenComponent<CascadingValue<MenuRadioGroup>>(3);
        builder.AddAttribute(4, "Value", this);
        builder.AddAttribute(5, "ChildContent", ChildContent);
        builder.CloseComponent();
        builder.CloseElement();
    }
}

/// <summary>role="menuitemradio" inside a <see cref="MenuRadioGroup"/>. Keeps the menu open by default.</summary>
public sealed class MenuRadioItem : MenuItemBase
{
    [CascadingParameter] private MenuRadioGroup? Group { get; set; }
    [Parameter, EditorRequired] public string Value { get; set; } = "";
    [Parameter] public bool CloseOnSelect { get; set; }

    protected override string Role => "menuitemradio";
    protected override bool? Checked => Group?.Value == Value;
    protected override bool ClosesMenu => CloseOnSelect;

    protected override Task OnActivateAsync() => Group?.SelectAsync(Value) ?? Task.CompletedTask;
}

/// <summary>role="separator" between groups of items.</summary>
public sealed class MenuSeparator : PrimitiveBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, Attributes("role"));
        builder.AddAttribute(2, "role", "separator");
        builder.CloseElement();
    }
}

/// <summary>
/// The menuitem that opens a <see cref="MenuSub"/> (aria-haspopup="menu", aria-expanded). Place it inside MenuSub, before
/// MenuSubContent.
/// </summary>
public sealed class MenuSubTrigger : MenuItemBase
{
    [CascadingParameter] internal MenuSub Sub { get; set; } = default!;

    protected override string Role => "menuitem";
    protected override bool ClosesMenu => false;

    protected override void OnInitialized()
    {
        if (Sub is null) throw new InvalidOperationException("MenuSubTrigger must be placed inside MenuSub.");
        base.OnInitialized();
        Sub.Context.TriggerId = ElementId;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender) Sub.Context.Trigger = Element;
    }

    protected override IEnumerable<KeyValuePair<string, object>>? ExtraAttributes =>
    [
        new("aria-haspopup", "menu"),
        new("aria-expanded", Sub.IsOpen ? "true" : "false"),
        new("aria-controls", Sub.Context.MenuId),
        new("data-submenu-trigger", ""),
    ];

    protected override Task OnActivateAsync() => Sub.OpenAsync(Menu.MenuFocus.First);

    protected override Task OnKeyDownAsync(KeyboardEventArgs e)
        => e.Key is "ArrowRight" or "Enter" or " " ? Sub.OpenAsync(Menu.MenuFocus.First) : Task.CompletedTask;

    protected override async Task OnPointerEnterAsync()
    {
        if (Disabled) return;
        await Sub.OpenAsync(null);
    }
}
