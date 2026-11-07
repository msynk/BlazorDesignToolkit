# Accessibility engineering

Target: WCAG 2.2 level AA for what automated checks can evaluate, with optional AAA contrast rules. Automated results —
including a clean axe scan — do not establish WCAG conformance. Keyboard behavior, screen-reader output, content and real
rendering need the manual checks listed below.

## Layers of verification

| Layer | Where | What | Runs in CI |
|---|---|---|---|
| Token contrast | `TokenValidator` / `bdt validate` | SC 1.4.3, 1.4.11 (and 1.4.6 when enabled) for configured pairs, pairings and focus indicators, per theme × mode × contrast × density; alpha compositing; indeterminate cases reported (out of gamut, translucent without backdrop, `none`). | Yes |
| Markup structure | `BlazorDesignToolkit.Testing.AccessibilityAudit` in bUnit tests | Duplicate ids, broken id references (`aria-labelledby/-describedby/-controls/-activedescendant/-errormessage`, `label[for]`), invalid roles and ARIA values, required states and owned elements, accessible names, list structure, focusable content inside `aria-hidden`, nested interactive content. | Yes |
| Component behavior | bUnit tests per primitive | State transitions, keyboard logic, controlled/uncontrolled behavior, disabled items, ids and relationships, disposal of JS resources. | Yes |
| Browser behavior | Playwright (`tests/BlazorDesignToolkit.BrowserTests`) | Real keyboard operation and focus movement (tabs, menu, select, combobox, listbox, tree, accordion, dialog focus containment and restoration, popover light dismiss, tooltip Escape and hover persistence, skip link, command menu), theme switching without reload, system color-scheme and contrast emulation, persisted theme on first response and without JavaScript, reflow at 320 CSS px (SC 1.4.10), toasts. Server and WebAssembly modes. | Yes (Chromium, Firefox and WebKit matrix; locally `BDT_BROWSER` picks the engine) |
| Accessibility tree | Playwright ARIA snapshots (`AssistiveTechnologyTests`) | The tree assistive technologies receive for menus, submenus, the menubar, sliders and the date picker dialog: roles, names (no decorative glyphs), states (expanded, selected, disabled, current) and structure; accessible descriptions and error messages of form fields. | Yes |
| Display adaptations | Playwright (`AssistiveTechnologyTests`) | Forced colors (Windows High Contrast; Chromium emulation): focus indicator, selected option in `Highlight`, slider thumb borders. Text spacing (SC 1.4.12) and 200% text (SC 1.4.4) on representative pages without clipped content or horizontal scrolling. | Yes |
| Rendered pages | axe-core via Deque.AxeCore.Playwright | Every demo page, plus open dialog, menu, select, combobox and toasts, with WCAG 2.0/2.1/2.2 A/AA tags, in light mode, in Ember dark + increased contrast + compact, and in WebAssembly mode. | Yes |

## Accessibility matrix

✔ automated · ◐ partially automated · ✎ manual review required

| Primitive | Roles/states | Keyboard (unit) | Keyboard (browser) | axe | Focus management | Screen readers |
|---|---|---|---|---|---|---|
| ToggleButton | ✔ | ✔ | ✔ | ✔ | n/a | ✎ |
| Checkbox (mixed) | ✔ | native | ✔ (both modes) | ✔ | n/a | ✎ |
| Switch | ✔ | ✔ | ✔ | ✔ | n/a | ✎ |
| RadioGroup | ✔ | native | ✔ (both modes) | ✔ | native | ✎ |
| Tabs | ✔ | ✔ | ✔ (both modes) | ✔ | ✔ | ✎ |
| Disclosure / Accordion | ✔ | ✔ | ✔ | ✔ | ✔ | ✎ |
| Dialog | ✔ | ✔ | ✔ containment, Escape, restoration, native close (both modes) | ✔ (open) | ✔ | ✎ |
| Popover | ✔ | ✔ | ✔ light dismiss, restoration, no-Popover-API fallback | ✔ | ✔ | ✎ |
| Tooltip | ✔ | n/a | ✔ focus, Escape, hover persistence | ✔ | n/a | ✎ |
| Listbox (incl. virtualized) | ✔ | ✔ | ✔ | ✔ | ✔ | ✎ |
| Select | ✔ | ✔ | ✔ (both modes) | ✔ (open) | ✔ | ✎ |
| Menu, submenus, menubar | ✔ | ✔ | ✔ (both modes) | ✔ (open) | ✔ | ✎ |
| Combobox | ✔ | ✔ | ✔ (both modes) | ✔ (open) | ✔ | ✎ (result announcements) |
| Command menu | ✔ | ✔ | ✔ | ✔ (open) | ✔ | ✎ |
| Tree view (incl. virtualized) | ✔ | ✔ | ✔ | ✔ | ✔ | ✎ |
| Slider / range slider | ✔ | ✔ | ✔ keys and pointer (both modes) | ✔ | ✔ | ✎ (value text) |
| Number field | ✔ | ✔ | ✔ | ✔ | ✔ | ✎ |
| Date picker | ✔ | ✔ | ✔ (both modes) | ✔ (open) | ✔ | ✎ (grid navigation) |
| Toasts / LiveRegion | ✔ | n/a | ✔ | ✔ | n/a | ✎ (announcement timing) |
| FormField | ✔ | n/a | ✔ | ✔ | n/a | ✎ |

## Regressions found during development

Each became a test:

| Defect | Found by | Regression test |
|---|---|---|
| Toast list items carried `role="status"`, breaking list semantics | axe (`list`) | `MenuAndFeedbackTests.Toasts_use_live_roles_dismiss_and_expire`, audit rule `list-children` |
| Enter on a menu item reopened the menu (the browser turned the same Enter into a click on the trigger that had just received focus; WebAssembly mode) | Playwright | `KeyboardTests.Menu_button_keyboard_contract(wasm)` |
| Menu "last item" was wrong when items were nested in a radio group (initialization order ≠ DOM order) | Playwright | `KeyboardTests.Menu_button_keyboard_contract` |
| Skip link did not move focus (Blazor intercepts same-page links) | Playwright | `KeyboardTests.Skip_link_moves_focus_to_main` |
| `label[for]` pointed at a non-labelable radio group / listbox | `AccessibilityAudit` | `ControlsTests.RadioGroup_uses_native_radios_and_reacts_to_controlled_value` |
| Tab `aria-controls` referenced unmounted panels | `AccessibilityAudit` | `CompositeTests.Tabs_render_roles_and_relationships` |
| Horizontal scrolling at 320px | Playwright reflow test | `ThemingBrowserTests.Layout_reflows_at_320_css_pixels_without_horizontal_scrolling` |
| Intermittent browser-test failures in all engines: Blazor's `FocusOnNavigate` focused the heading after the test had already focused a control | Event tracing in WebKit | `PageExtensions.OpenAsync` waits for the initial heading focus |
| Decorative glyphs from CSS (`▸`, `✓`, `+`) became part of accessible names ("Send to ▸") | Playwright ARIA snapshot | `AssistiveTechnologyTests.Accessibility_tree_of_menus_menubar_and_inputs` (starter styles use `content: "▸" / ""`) |
| Date picker weekday headers were named "S", "M", "T"… (ambiguous) | Playwright ARIA snapshot | `AssistiveTechnologyTests.Accessibility_tree_of_the_date_picker_dialog` |
| In engines without the Popover API, closed `[popover]` elements were never hidden | Browser test with the API removed | `NewPrimitivesBrowserTests.Overlays_work_without_the_popover_api` |
| Focus fell to `<body>` in a virtualized tree after expanding (rows are re-mapped while scrolling) | Playwright | `NewPrimitivesBrowserTests.Virtualized_tree_moves_focus_to_rows_outside_the_rendered_window` (virtualized trees use `aria-activedescendant`) |
| A controlled component kept a value its parent rejected | bUnit | `ControlsTests.Controlled_components_revert_changes_the_parent_does_not_accept` |
| Characters typed quickly into the combobox/command menu were lost under Interactive Server (a render in flight overwrote `value`; fixed with `@bind:get`/`@bind:set`) | Intermittent Playwright failure | `KeyboardTests.Command_menu_opens_with_shortcut_and_navigates` |

## Manual review checklist (per release)

Automated checks cover what can be computed (the accessibility tree, contrast, structure, keyboard behavior, display
adaptations). Whether speech output is understandable, announcements are timely and the experience is efficient can only
be judged with real assistive technologies. Before a release:

- NVDA + Firefox/Chrome, JAWS + Chrome, VoiceOver + Safari (macOS, iOS), TalkBack + Chrome: names, roles, states and
  announcements of every primitive; result-count and toast announcements; slider value text; date picker grid navigation
  and month announcements; menubar and submenu navigation.
- Windows High Contrast with a real theme (the automated test emulates forced colors in Chromium only).
- 400% zoom on a phone-sized viewport, long translated labels (automated: 320 px reflow, 200% text, text spacing).
- Touch-only and pointer-only operation; target sizes (SC 2.5.8) of your styled controls.
- `prefers-reduced-motion`: animations in your styles respect it (starter styles do).
- Colors the engine reports as indeterminate (BDT5005) and text over images or gradients.

## Running the checks

```bash
dotnet test tests/BlazorDesignToolkit.Primitives.Tests        # markup, behavior, audit rules
dotnet test tests/BlazorDesignToolkit.BrowserTests            # Playwright + axe, ARIA snapshots, display adaptations
BDT_BROWSER=firefox dotnet test tests/BlazorDesignToolkit.BrowserTests
dotnet bdt validate --config samples/bdt.config.json          # token contrast in every theme combination
```

Use `AccessibilityAssert.NoViolations(cut.Markup)` in your own bUnit tests; it fails with a readable list of issues.
