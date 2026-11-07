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
- **Binding.** `@bind-Value`, `@bind-Open`, `@bind-Checked`… work as with Blazor inputs: the component keeps its value,
  raises `…Changed`, and adopts a new value when the parent passes one. `Default…` parameters set the initial value.
  Text inputs (`Combobox`, `CommandMenu`) and `Checkbox` bind their DOM value with `@bind:get`/`@bind:set`, so text typed
  while a server render is in flight is never overwritten, and a checkbox whose change the parent rejects shows its
  real state again. `RadioGroup` is the exception: if a controlled parent rejects a selection, the browser keeps showing
  the clicked radio as checked until the selection changes (Blazor cannot track native radio state).
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
- **Limitation:** some browsers close a dialog on a repeated Escape even when closing is prevented (anti-abuse close
  watchers); keep `CloseOnEscape` true unless data loss must be prevented.

## Popover

- **Components:** `Popover` (`Open`/`OpenChanged`, `DefaultOpen`, `Placement` such as `bottom-start`, `top-center`),
  `PopoverTrigger` (`aria-haspopup="dialog"`, `aria-expanded`, `aria-controls`), `PopoverContent` (`popover="auto"`,
  `role="dialog"` labelled by the trigger unless labelled otherwise, `FocusOnOpen`=true, `KeepMounted`).
- **Behavior:** top layer, native light dismiss (Escape, outside click); positioned next to the trigger and flipped when
  space is short; focus moves into the content and returns to the trigger when closed from inside.
- **Keyboard:** Enter/Space open; Tab moves through content; Escape closes.
- **Limitation:** without Popover API support (pre-2024 browsers) a fallback positions the element in place and emulates
  light dismiss, without top-layer stacking.

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
- **Keyboard:** ↑/↓ (←/→ horizontal), Home/End, type-to-select; Space toggles (multiple); Shift+↑/↓ extends; Ctrl/⌘+A
  selects all. In single selection with `SelectionFollowsFocus`, focusing the listbox selects the first option when none is.

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
  Enter/Space select; `*` expands siblings; type-to-select.

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
