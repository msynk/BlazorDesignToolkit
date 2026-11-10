namespace BlazorDesignToolkit.Demo.Client;

/// <summary>One headless component of BlazorDesignToolkit.Primitives, as presented by the demo.</summary>
/// <param name="Name">Component name.</param>
/// <param name="Category">Group on the primitives overview.</param>
/// <param name="Pattern">The WAI-ARIA Authoring Practices pattern or native element it implements.</param>
/// <param name="Page">The demo page that shows it.</param>
public sealed record PrimitiveInfo(string Name, string Category, string Pattern, string Page);

/// <summary>Facts about the toolkit that several demo pages present.</summary>
public static class DemoCatalog
{
    public static readonly IReadOnlyList<PrimitiveInfo> Primitives =
    [
        new("ToggleButton", "Buttons and choices", "Button (toggle), aria-pressed", "primitives/controls"),
        new("Switch", "Buttons and choices", "Switch", "primitives/controls"),
        new("Checkbox", "Buttons and choices", "Native checkbox, mixed state", "primitives/controls"),
        new("RadioGroup", "Buttons and choices", "Native radio group", "primitives/controls"),
        new("FormField", "Forms", "Label, description and error wiring", "primitives/forms"),
        new("NumberField", "Forms", "Spinbutton", "primitives/inputs"),
        new("Slider", "Forms", "Slider", "primitives/inputs"),
        new("RangeSlider", "Forms", "Multi-thumb slider", "primitives/inputs"),
        new("DatePicker", "Forms", "Date picker dialog with grid", "primitives/inputs"),
        new("Tabs", "Disclosure and navigation", "Tabs", "primitives/tabs"),
        new("Disclosure", "Disclosure and navigation", "Disclosure", "primitives/disclosure"),
        new("Accordion", "Disclosure and navigation", "Accordion", "primitives/disclosure"),
        new("TreeView", "Disclosure and navigation", "Tree view (virtualizable)", "primitives/tree"),
        new("Dialog", "Overlays", "Dialog (modal) and alert dialog", "primitives/dialog"),
        new("Popover", "Overlays", "Non-modal popover (Popover API)", "primitives/popover"),
        new("Tooltip", "Overlays", "Tooltip", "primitives/popover"),
        new("Listbox", "Selection", "Listbox (virtualizable, multi-select)", "primitives/listbox"),
        new("Select", "Selection", "Select-only combobox", "primitives/listbox"),
        new("Combobox", "Selection", "Editable combobox with list autocomplete", "primitives/listbox"),
        new("Menu", "Menus and commands", "Menu button", "primitives/menu"),
        new("MenuSub", "Menus and commands", "Submenu", "primitives/menu"),
        new("Menubar", "Menus and commands", "Menubar", "primitives/menu"),
        new("CommandMenu", "Menus and commands", "Command palette (combobox + listbox)", "primitives/feedback"),
        new("ToastRegion", "Feedback", "Notifications in a labelled region", "primitives/feedback"),
        new("LiveRegion", "Feedback", "Live region announcements", "primitives/feedback"),
        new("RovingFocusGroup", "Utilities", "Roving tabindex for composite widgets", "primitives"),
        new("FocusTrap", "Utilities", "Focus containment", "primitives"),
        new("VisuallyHidden", "Utilities", "Text for assistive technology only", "primitives"),
        new("DynamicHeading", "Utilities", "Heading with a configurable level", "primitives"),
    ];

    /// <summary>Formats the exporters can write (bdt export / build).</summary>
    public static readonly IReadOnlyList<(string Format, string Description)> ExportFormats =
    [
        ("css", "CSS custom properties, resolved or with var() references"),
        ("theme-css", "Every brand and axis combination in one stylesheet"),
        ("dtcg-json", "Resolved, self-contained DTCG documents"),
        ("tailwind", "Tailwind CSS 4 @theme mappings"),
        ("tailwind-v3", "Tailwind CSS 3 preset"),
        ("bootstrap-scss", "Bootstrap 5.3 Sass variables"),
        ("bootstrap-css", "Bootstrap --bs-* runtime overrides"),
        ("bootstrap-themes", "Bootstrap variables per theme and mode, with a component bridge"),
        ("fluent", "Fluent 2 design token properties"),
    ];
}
