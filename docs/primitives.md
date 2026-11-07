# Headless primitives: API and accessibility contracts

Package: `BlazorDesignToolkit.Primitives` (Blazor only; no styling, no token or framework dependency).
Optional CSS: `BlazorDesignToolkit.Primitives.StarterStyles` (`_content/BlazorDesignToolkit.Primitives.StarterStyles/bdt-starter.css`).

## Common conventions

- **Semantic HTML first.** Native `<button>`, `<input type=checkbox|radio>`, `<dialog>` and the Popover API are used
  wherever they provide the right behavior. Prefer native `<select>` and `<details>` when they meet your needs.
- **Attributes.** Unmatched attributes (`class`, `style`, `data-*`, `aria-label`, `id`…) go to the main element. State and
  relationship attributes the primitive manages (`aria-selected`, `aria-controls`, `role`, …) always win.
- **Styling hooks.** `data-state` (`open|closed`, `checked|unchecked|indeterminate`, `active|inactive`, `on|off`),
  `data-disabled`, `data-highlighted`, `data-orientation`, `data-side` (placed popups), plus ARIA attributes. No class names
  are imposed.
- **Binding.** `@bind-Value`, `@bind-Open`, `@bind-Checked`… work as with Blazor inputs. A component is **controlled** when
  the parent passes the value *and* handles the change: a change the parent does not accept (it re-renders with the old
  value) is reverted, including the native state of checkboxes and radios. With only `Default…` and/or `…Changed` the
  component is **uncontrolled**: it keeps its own value and reports changes. Text inputs (`Combobox`, `CommandMenu`,
  `NumberField`, `DatePicker`) and `Checkbox` bind their DOM value with `@bind:get`/`@bind:set`, so text typed while a
  server render is in flight is never overwritten.
- **Texts.** Built-in accessible names, announcements and empty states come from `PrimitiveStrings` (English). Derive from
  it, override what you need (from resources or an `IStringLocalizer`, using `CultureInfo.CurrentUICulture`), and register
  it: `services.AddHeadlessPrimitives().AddSingleton<PrimitiveStrings, MyStrings>()` (scoped for per-user cultures).
  Component parameters (`Label`, `EmptyText`, …) still win for one instance. Number and date formats follow the
  component's `Culture` (default: the current culture).
- **Ids.** Generated (`bdt-…`) and unique; pass `id`/`Id` to control them. All id references are kept consistent.
- **Render modes.** Interactive Server and Interactive WebAssembly are supported and tested. During static rendering and
  prerendering the markup is correct but not interactive (Disclosure panels, for example, keep their initial state).
- **JavaScript.** One isolated module, loaded on demand; used only for key-default prevention, focus moves into overlays,
  dialogs, popover placement, tooltips, focus trapping and global shortcuts. Disconnected circuits and prerendering are
  tolerated.
- **Forms.** `FormField` wires `label[for]`, `aria-describedby`, `aria-invalid`, `aria-errormessage`, `required` and
  `disabled` for `Checkbox`, `Switch`, `RadioGroup`, `Listbox`, `Select`, `Combobox`, `FieldInput` and any custom control
  (`Context="field"` + `@attributes="field.ControlAttributes"`).
- Server interactivity adds a network round trip to keyboard handling; behavior stays correct but feedback is delayed on
  slow connections.

Keyboard tables below follow the WAI-ARIA Authoring Practices Guide (APG). WCAG references are WCAG 2.2.

---

## ToggleButton

- **Markup:** `<button type="button" aria-pressed="true|false">`.
- **Parameters:** `Pressed`/`PressedChanged`, `DefaultPressed`, `Disabled`, `ChildContent`.
- **Keyboard:** Enter, Space toggle (native).
- **Name:** button content or `aria-label`; do not change the label with the state.
- **WCAG:** 4.1.2 Name, Role, Value; 2.1.1 Keyboard.

## Checkbox

- **Markup:** native `<input type="checkbox">`; `indeterminate` DOM property for the mixed state (set through JS).
- **Parameters:** `Checked`/`CheckedChanged`, `DefaultChecked`, `Indeterminate`/`IndeterminateChanged`, `Disabled`,
  `Required`, `Name`, `Value`.
- **Keyboard:** Space toggles (native). **Name:** `<label>` (wrapping or `FormLabel`).
- **WCAG:** 1.3.1, 3.3.2 Labels or Instructions, 4.1.2.

## Switch

- **Markup:** `<button type="button" role="switch" aria-checked>`; optional hidden input for form posts (`Name`, `Value`).
- **Parameters:** `Checked`/`CheckedChanged`, `DefaultChecked`, `Disabled`, `Required`, `Name`, `Value`, `ChildContent` (visual only).
- **Keyboard:** Space, Enter toggle. **Name:** `FormLabel` (aria-labelledby) or `aria-label` — required.
- **WCAG:** 4.1.2; 1.4.11 for the thumb/track (style responsibility).

## RadioGroup / Radio

- **Markup:** `role="radiogroup"` container (labelled by the field label or `aria-label`) with native radio inputs that
  share a generated `name`.
- **Parameters:** `RadioGroup<TValue>`: `Value`/`ValueChanged`, `DefaultValue`, `Name`, `Disabled`, `Required`,
  `Orientation`. `Radio<TValue>`: `Value`, `Disabled`.
- **Keyboard (native):** Tab enters the group at the checked radio; arrow keys move and select; Space selects.
- **Limitation:** the label of a group is referenced with `aria-labelledby` (not `for`), because a group is not labelable.

## Tabs

- **Components:** `Tabs` (`Value`/`ValueChanged`, `DefaultValue`, `Orientation`, `Activation` = Automatic|Manual,
  `Direction`, `Loop`), `TabList` (needs `aria-label`/`aria-labelledby`), `Tab` (`Value`, `Disabled`), `TabPanel`
  (`Value`, `KeepMounted`, `Focusable`).
- **Markup:** `role=tablist` (`aria-orientation`), `role=tab` buttons (`aria-selected`, `aria-controls`, roving `tabindex`),
  `role=tabpanel` (`aria-labelledby`, `hidden` when inactive). Panel elements always exist so `aria-controls` resolves;
  inactive content is unmounted unless `KeepMounted`.
- **Keyboard:**

| Key | Behavior |
|---|---|
| Tab | Into the tab list (selected tab), then into the panel |
| ←/→ (horizontal), ↑/↓ (vertical) | Previous/next tab, wrapping, skipping disabled tabs; mirrored for RTL |
| Home / End | First / last tab |
| Enter / Space | Select (manual activation) |

- **Disabled:** `aria-disabled`, not selectable, skipped. **WCAG:** 1.3.1, 2.1.1, 2.4.3 Focus Order, 4.1.2.

## Disclosure / Accordion

- **Disclosure:** `Disclosure` (`Open`/`OpenChanged`, `DefaultOpen`, `Disabled`), `DisclosureTrigger` (button,
  `aria-expanded`, `aria-controls`), `DisclosurePanel` (`hidden` when closed, `KeepMounted`). Enter/Space toggle.
- **Accordion:** `Accordion` (`Type` Single|Multiple, `Collapsible`, `Values`/`ValuesChanged`, `DefaultValues`,
  `HeadingLevel` 1–6, `ArrowNavigation`), `AccordionItem` (`Value`, `Disabled`), `AccordionTrigger` (a button inside an
  `<hN>`), `AccordionPanel` (`role=region` labelled by the trigger; `UseRegion=false` for many items).
- **Keyboard:** Enter/Space toggle; ↑/↓ move between headers; Home/End first/last header.
- **States:** the open item of a non-collapsible single accordion is `aria-disabled="true"` (APG).
- **WCAG:** 1.3.1 (headings), 2.1.1, 4.1.2.

## Dialog

- **Components:** `Dialog` (`Open`/`OpenChanged`, `DefaultOpen`, `Modal`=true, `Alert`, `CloseOnEscape`=true,
  `CloseOnBackdropClick`=false, `LockScroll`=true, `InitialFocus` selector), `DialogTrigger` (`aria-haspopup="dialog"`,
  `aria-expanded`, `aria-controls`), `DialogContent` (native `<dialog>`, `KeepMounted`), `DialogTitle` (`Level`; becomes
  `aria-labelledby`), `DialogDescription` (`aria-describedby`), `DialogClose` (`OnClick` runs before closing).
- **Behavior:** modal dialogs use `showModal()`: the rest of the page is inert, the dialog is in the top layer, focus moves
  to the first focusable element (or `autofocus`/`InitialFocus`) and returns to the previously focused element on close.
  Escape requests closing (honoring `CloseOnEscape`). `Alert` sets `role="alertdialog"` — put `autofocus` on the least
  destructive action.
- **Keyboard:** Tab/Shift+Tab within the dialog; Escape closes.
- **WCAG:** 2.1.2 No Keyboard Trap (Escape and buttons always close), 2.4.3, 4.1.2.
- **`CloseOnEscape=false`:** Escape is ignored. Browsers' close watchers may close a dialog on a repeated Escape even when
  closing is prevented; the dialog then reopens immediately and focus returns to where it was, so the component's state
  stays authoritative. With `CloseOnEscape=true`, a dialog closed by the browser updates `Open` accordingly.

## Popover

- **Components:** `Popover` (`Open`/`OpenChanged`, `DefaultOpen`, `Placement` such as `bottom-start`, `top-center`),
  `PopoverTrigger` (`aria-haspopup="dialog"`, `aria-expanded`, `aria-controls`), `PopoverContent` (`popover="auto"`,
  `role="dialog"` labelled by the trigger unless labelled otherwise, `FocusOnOpen`=true, `KeepMounted`).
- **Behavior:** top layer, native light dismiss (Escape, outside click); positioned next to the trigger and flipped when
  space is short; focus moves into the content and returns to the trigger when closed from inside.
- **Keyboard:** Enter/Space open; Tab moves through content; Escape closes.
- **Without the Popover API** (pre-2024 browsers) a fallback hides closed popups, positions open ones in place (fixed,
  high z-index) and emulates light dismiss (outside pointer press, Escape). There is no top layer, so an ancestor with
  `overflow: hidden` or a transform can clip the popup. Covered by a browser test that removes the API.

## Tooltip

- **Components:** `Tooltip` (`ShowDelay`=300, `HideDelay`=150, `Placement`), `TooltipTrigger` (a button with
  `aria-describedby`), `TooltipContent` (`role="tooltip"`, `popover="manual"`).
- **Behavior (browser-side, no round trips):** hover shows after the delay, focus shows immediately; Escape hides without
  moving focus; the pointer can move onto the tooltip; it stays until hover/focus ends (WCAG 1.4.13 Content on Hover or Focus).
- **Rules:** the tooltip is a description; the trigger needs its own name. No interactive content — use a Popover.

## Listbox

- **Component:** `Listbox<TItem>` — `Items`, `ItemLabel`, `ItemDisabled`, `ItemTemplate` (`OptionContext<TItem>`),
  `Value`/`ValueChanged`, `DefaultValue`, `Multiple`, `Values`/`ValuesChanged`, `DefaultValues`, `SelectionFollowsFocus`=true,
  `Orientation`, `Disabled`, `Loop`.
- **Markup:** focusable `role=listbox` with `aria-activedescendant`, `aria-multiselectable`; options `role=option`,
  `aria-selected`, `aria-disabled`; the active option scrolls into view.
- **Keyboard:** ↑/↓ (←/→ horizontal), Home/End, Page Up/Page Down (`PageSize`=10), type-to-select; Space toggles
  (multiple); Shift+↑/↓ extends; Ctrl/⌘+A selects all. In single selection with `SelectionFollowsFocus`, focusing the
  listbox selects the first option when none is.
- **Virtualization:** `Virtualize`=true renders only the options in view (Blazor `Virtualize`; `ItemSize` in CSS px, uniform
  heights; `OverscanCount`). The listbox must be the scroll container (a height or `max-height` and `overflow: auto`; the
  starter styles do this). Options carry `aria-setsize`/`aria-posinset`, and keyboard moves scroll the target option into
  the rendered range before it becomes the active descendant. Tested with 10,000 options.

## Select

- **Component:** `Select<TItem>` — `Items`, `ItemLabel`, `ItemValue`, `ItemDisabled`, `ItemTemplate`, `ValueTemplate`,
  `Value`/`ValueChanged`, `DefaultValue`, `Placeholder`, `Name` (hidden input), `Disabled`, `Required`, `Placement`,
  `PopupAttributes`.
- **Markup:** APG select-only combobox: `<button role="combobox" aria-haspopup="listbox" aria-expanded aria-controls
  aria-activedescendant>` and a `role=listbox` popup in the top layer. Labelled by the field label and the current value.

| Key | Closed | Open |
|---|---|---|
| ↓ / Enter / Space | Open (selected or first option) | ↓ moves; Enter/Space select and close |
| ↑ | Open on the last option | Move up; Alt+↑ selects and closes |
| Home / End | Open on first / last | First / last option |
| Type | Open on the matching option | Move to the matching option |
| Escape | — | Close without changing the value |
| Tab | — | Select the highlighted option, close, move on |

## Menu

- **Components:** `Menu` (`Open`/`OpenChanged`, `Placement`), `MenuTrigger` (`aria-haspopup="menu"`, `aria-expanded`,
  `aria-controls`), `MenuContent` (`role=menu`, labelled by the trigger), `MenuItem` (`OnSelect`, `Disabled`, `TextValue`,
  `KeepOpen`), `MenuCheckboxItem` (`IsChecked`/`IsCheckedChanged`, `CloseOnSelect`), `MenuRadioGroup` (`Value`/`ValueChanged`,
  `aria-label`) + `MenuRadioItem` (`Value`), `MenuSeparator`.
- **Keyboard:** Enter/Space/↓ on the trigger open and focus the first item, ↑ the last; ↑/↓ wrap and skip disabled items;
  Home/End; type-to-select (`TextValue`); Enter/Space activate (checkbox/radio items keep the menu open); Escape closes and
  returns focus to the trigger; Tab closes and moves on.
- **Focus:** real DOM focus moves between items (roving). Item order is synchronized with the DOM, so items nested in
  other components keep the visual order.
- **Submenus:** `MenuSub` containing `MenuSubTrigger` (a `menuitem` with `aria-haspopup="menu"`, `aria-expanded`,
  `aria-controls`) and `MenuSubContent` (the nested `role=menu`, labelled by its trigger; `Placement`="right-start"). → / Enter /
  Space open the submenu on its first item; ← / Escape close it and return focus to the trigger; activating any item closes
  every menu up to the root; Tab closes all. Hovering the trigger opens it without moving focus; hovering a sibling closes it.
  Submenus nest.

## Menubar

- **Components:** `Menubar` (`role=menubar`, horizontal; give it an `aria-label`) containing `Menu`s whose trigger is a
  `MenubarTrigger` (`role=menuitem`, `aria-haspopup="menu"`, `aria-expanded`, roving `tabindex`).
- **Keyboard:** ←/→ move between triggers (wrapping) and, while a menu is open, open the adjacent menu on its first item
  (also from an item without a submenu); Home/End first/last trigger; ↓/Enter/Space open on the first item, ↑ on the last;
  Escape closes and returns focus to the trigger. Only one menu of a menubar is open; while one is open, hovering another
  trigger switches to it.

## Combobox

- **Component:** `Combobox<TItem>` — `Items`, `ItemLabel`, `ItemDisabled`, `ItemTemplate`, `EmptyContent`,
  `Value`/`ValueChanged`, `TextChanged` (for server-side search), `Filter` (default: case-insensitive contains; `null`
  disables built-in filtering), `AutoHighlight`=true, `AllowCustomValue`, `Disabled`, `Required`, `Placement`.
- **Markup:** `<input role="combobox" aria-autocomplete="list" aria-expanded aria-controls aria-activedescendant>` and a
  listbox popup; a polite live region announces "N results available".
- **Keyboard:** typing filters and opens; ↓ opens/moves (Alt+↓ opens only); ↑ moves; Enter selects; Escape closes, then
  clears; Tab closes. Without `AllowCustomValue`, the text reverts to the selection on blur.

## Command menu

- **Component:** `CommandMenu` — `Commands` (`CommandItem(Id, Label, Execute)` with `Group`, `Keywords`, `Shortcut`,
  `Disabled`), `Hotkey`="Ctrl+K" (⌘ on macOS), `Open`/`OpenChanged`, `Label`, `Placeholder`, `EmptyText`.
- **Markup:** a modal Dialog with a combobox input and a grouped listbox (`role=group` labelled by group headers); result
  counts are announced. ↑/↓ move, Enter runs and closes, Escape closes and restores focus.

## Tree view

- **Component:** `TreeView<TItem>` — `Items`, `ChildrenSelector`, `ItemLabel`, `ItemDisabled`, `ItemTemplate`
  (`TreeItemContext<TItem>`), `Value`/`ValueChanged`, `ExpandedItems`/`ExpandedItemsChanged`. Needs `aria-label`.
- **Markup:** `role=tree`, `role=treeitem` with `aria-level`, `aria-setsize`, `aria-posinset`, `aria-expanded` (parents
  only), `aria-selected`; children in `role=group`; one item in the tab sequence.
- **Keyboard:** ↑/↓ visible items; → expands or moves to the first child; ← collapses or moves to the parent; Home/End;
  Page Up/Page Down (`PageSize`=10); Enter/Space select; `*` expands siblings; type-to-select.
- **Virtualization:** `Virtualize`=true renders the visible rows as a flat list of treeitems (valid ARIA, since every row has
  `aria-level`/`aria-setsize`/`aria-posinset`) and only those in view (`ItemSize`, uniform heights). Rows come and go while
  scrolling, so focus stays on the tree and the active row is `aria-activedescendant` (`data-highlighted`); indent rows
  with `--bdt-tree-level`. Tested with 2,000 nodes.

## Slider

- **Components:** `Slider` (`Value`/`ValueChanged`, `DefaultValue`, `ValueCommitted`, `Min`=0, `Max`=100, `Step`=1, `LargeStep`
  (default a tenth of the range), `Orientation`, `Direction`, `Disabled`, `Label`, `ValueText`, `Name`) and `RangeSlider`
  (`Start`/`End` with their `…Changed` and `Default…`, `MinDistance`, `StartLabel`/`EndLabel`).
- **Markup:** a focusable `role=slider` thumb with `aria-valuenow/min/max`, `aria-valuetext` (from `ValueText`, for values that
  need units or words), `aria-orientation`; labelled by the FormField label. The range slider is a `role=group` with two
  sliders whose min/max are constrained by each other (APG multi-thumb slider). Styling: `--bdt-slider-value` (or
  `--bdt-slider-start`/`--bdt-slider-end`) as percentages, `data-dragging`.
- **Keyboard:** →/↑ increase, ←/↓ decrease (horizontal arrows reversed in RTL), Page Up/Down by `LargeStep`, Home/End min/max.
- **Pointer:** press or drag anywhere on the slider; the browser reports the position once per animation frame and the
  nearest thumb follows. `ValueCommitted` fires on release and after each key press.

## Number field

- **Component:** `NumberField` — `Value`/`ValueChanged` (`double?`), `DefaultValue`, `Min`, `Max`, `Step`=1, `LargeStep`,
  `Nullable`=true, `Format` (.NET format string), `Culture`, `ShowButtons`=true, `ReadOnly`, `Required`, `ValueText`, `Name`.
- **Markup:** APG spinbutton: an editable `<input role="spinbutton" inputmode="decimal">` with `aria-valuenow/min/max` and
  `aria-valuetext`; increment/decrement buttons are not in the tab sequence and control the input (`aria-controls`).
- **Keyboard:** ↑/↓ by `Step`, Page Up/Down by `LargeStep`. Typed text is parsed with the culture on Enter or blur; values
  are clamped and snapped to `Min + n × Step`; text that is not a number reverts. Home/End move the caret (the text is
  editable), as is common for editable spinbuttons.

## Date picker

- **Component:** `DatePicker` — `Value`/`ValueChanged` (`DateOnly?`), `DefaultValue`, `Min`, `Max`, `IsDateDisabled`, `Culture`,
  `Today`, `Direction`, `Disabled`, `Required`, `Label`, `Name`, `Placeholder`, `ButtonContent`, `ShowKeyboardHelp`.
- **Markup:** APG Date Picker Dialog: a text input (described by the culture's date pattern), a button
  (`aria-haspopup="dialog"`, named "Choose date" plus the selected date) and a modal `role=dialog` with month/year
  navigation buttons, a heading that is a polite live region, and a `role=grid` month: weekday column headers named with
  full day names, day cells named with the full date, `aria-selected`, `aria-current="date"`, `aria-disabled`.
- **Keyboard (grid):** ←/→ day (RTL-aware), ↑/↓ week, Home/End start/end of week, Page Up/Down month, Shift+Page Up/Down
  year, Enter/Space choose and close, Escape close; Tab cycles inside the dialog; focus returns to the button.
  Alt+↓ in the input opens the calendar.
- **Text entry:** parsed with the culture's short date pattern (then any culture format) on Enter or blur; invalid or
  unavailable dates keep the text and set `aria-invalid`.

## Toasts and announcements

- **Services:** `services.AddHeadlessPrimitives()` registers `ToastService` and `LiveAnnouncer`.
- **`ToastRegion`** (render once): a labelled `<section>` with a list; each toast's content is `role="status"`
  (`role="alert"` for errors) inside its list item; dismiss button with `aria-label`; timers pause while hovered or focused
  (WCAG 2.2.1 Timing Adjustable); errors stay until dismissed; optional action button; `ToastTemplate` for custom markup.
- **`LiveRegion`** (render once, early): visually hidden polite and assertive regions; `LiveAnnouncer.Announce(message,
  Politeness)` re-announces repeated messages by clearing first (WCAG 4.1.3 Status Messages).

## Form fields

`FormField` (`Id`, `Invalid`, `Required`, `Disabled`, `ChildContent` with `FormFieldContext`), `FormLabel`,
`FormDescription`, `FormError` (rendered only while invalid; `Announce` adds `aria-live="polite"`), `FieldInput`
(`@bind-Value`, `Type`). WCAG 1.3.1, 3.3.1 Error Identification, 3.3.2 Labels or Instructions, 4.1.2.

## Utilities

- `RovingFocusGroup` (`Role`, `Orientation`, `Direction`, `Loop`) + `RovingFocusItem` (`Element`, `Disabled`): toolbar pattern.
- `FocusTrap` (`Active`, `RestoreFocus`) for custom non-native overlays.
- `VisuallyHidden` (`Focusable`), `DynamicHeading` (`Level`).
- Pure helpers: `KeyboardNavigation`, `Typeahead`, `OptionList<T>`, `CompositeRegistry<T>`, `Ids`.
- Portals are not provided: overlays use the browser's top layer (`<dialog>`, Popover API) and are never moved in the DOM.
