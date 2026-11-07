# Implementation status

State of the toolkit as of version `0.1.0-preview.1` (November 2026). "Done" means implemented, covered by automated
tests and exercised by the demo app. Items that are only partly done or not done are listed as such; nothing here is a
stub presented as finished.

## Summary

| Area | Status | Packages / projects |
|---|---|---|
| A. Core token engine (DTCG 2025.10) | Done | `BlazorDesignToolkit.Tokens` |
| B. Runtime theming | Done | `BlazorDesignToolkit.Theming`, `BlazorDesignToolkit.Theming.Blazor` |
| C. Headless primitives, tier 1 | Done | `BlazorDesignToolkit.Primitives` |
| C. Headless primitives, tier 2 | Done (no virtualization) | `BlazorDesignToolkit.Primitives` |
| Starter styles | Done | `BlazorDesignToolkit.Primitives.StarterStyles` |
| D. Accessibility and audit tooling | Done (automated layers); manual screen-reader review not performed | `BlazorDesignToolkit.Tokens` (contrast), `BlazorDesignToolkit.Testing`, browser tests |
| E. Integrations | Done: plain CSS, Tailwind v4, Bootstrap 5.3, Fluent UI Blazor (catalog subset) | `BlazorDesignToolkit.Tokens.Tailwind`, `.Bootstrap`, `.Fluent` |
| CLI | Done | `BlazorDesignToolkit.Cli` (`bdt`) |
| Demo app | Done (Interactive Server and WebAssembly) | `samples/BlazorDesignToolkit.Demo`, `.Demo.Client` |
| Documentation | Done | `docs/`, package READMEs |
| Benchmarks | Done (baselines recorded, no enforced thresholds) | `benchmarks/BlazorDesignToolkit.Benchmarks`, [performance.md](performance.md) |
| CI | Done (build, tests, quality gates, pack, 3-browser matrix) | `.github/workflows/ci.yml` |
| Packaging | Packages build; not published | 10 NuGet packages |

## A. Core token engine

Done:

- Source-preserving JSON parser with line/column locations, size and depth limits, cancellation, duplicate-key detection,
  optional comments/trailing commas.
- DTCG model: groups, tokens, `$type` inheritance (explicit → reference → group), `$description`, `$extensions`,
  `$deprecated`, `$extends` (with cycle detection), `$root`, all 13 types, all 14 color spaces.
- References: curly aliases and JSON Pointer `$ref`, including composite sub-values; reference type checking; chains and
  cycles reported with the full chain; configurable maximum depth.
- Resolver module: sets, modifiers, `resolutionOrder`, inputs, `$ref` to sets; sandboxed file loader (no remote access).
- Value parsing for every type, with optional legacy formats (string colors, string dimensions) behind an option.
- Validation rules: naming, required tokens, duplicate values, scale consistency, deprecation, layer rules, contrast.
- Diagnostics: stable ids (BDT1001–BDT9004), severities, locations, remediation text, policy (severity overrides,
  suppressions with mandatory justification, fail-on threshold), JSON/GitHub/SARIF output.
- Export: DTCG JSON round-trip, CSS custom properties (resolved or `var()` references, color formats, layers), exporter
  registry, coverage analysis against component manifests, token diff.

Gaps:

- `$extends` targeting a group that exists only through another extension is reported (BDT2013), not resolved.
- JSON Pointers are not percent-decoded.
- Hex color output clips out-of-gamut colors (reported as BDT7003); no CSS Color 4 gamut mapping.

## B. Runtime theming

Done:

- Theme catalog: brands, axes (mode, contrast, density, custom), layer composition, inheritance between themes, `system`
  values mapped to media queries, completeness analysis.
- Composition with runtime overrides; diagnostics for invalid overrides.
- Stylesheet generation: cascade-diff output (only changed declarations per axis value), `:where()` selectors, media-query
  duplicates for `system`, `color-scheme`, verified by a cascade evaluator against every combination.
- `ThemeManager`: selection, batching, persistence abstraction (none, localStorage, cookie), failure reporting.
- Blazor adapter: `ThemeStyles`, `ThemeInitScript` (no-flash startup), `ThemeProvider`, `ThemeScope`, JS module for
  system-preference changes; prerender-safe root attributes from the cookie; works under Interactive Server and
  WebAssembly.

Gaps:

- Runtime overrides are written as inline custom properties on the document root; `ThemeScope` switches theme and axes
  for a subtree but does not take overrides.

## C. Headless primitives

Done (tier 1): ToggleButton, Checkbox (with mixed state), Switch, RadioGroup/Radio, Tabs, Disclosure, Accordion, Dialog
(native `<dialog>`), Popover (Popover API with fallback), Tooltip, Listbox, Select, Menu (with checkbox items, radio groups and
separators), RovingFocusGroup.

Done (tier 2): Combobox (filtering, server-side search hook, result announcements), CommandMenu (global shortcut), TreeView
(data-driven, typeahead, expand/collapse), LiveRegion/LiveAnnouncer, ToastRegion/ToastService, FormField (label,
description, error wiring), FocusTrap, VisuallyHidden, DynamicHeading. No portal component: overlays use the browser top
layer (`<dialog>` and the Popover API), which removes the need for one.

Gaps:

- No virtualization for very long listboxes/trees.
- A controlled `RadioGroup` that rejects a selection leaves the clicked radio checked in the browser until the
  selection changes.
- No submenus, menubar, slider, number field or date picker.
- Result-count and toast announcement texts are localizable by parameter; other built-in strings (for example the default
  combobox empty text) are parameters as well, but no resource-based localization is shipped.

## D. Accessibility and audit tooling

Done: WCAG 2.2 contrast (AA, optional AAA, non-text 3:1, focus indicators) with alpha compositing and indeterminate
results; markup audit (`AccessibilityAudit`) used in every bUnit test; Playwright keyboard/focus tests in Server and
WebAssembly modes; axe-core scans of every demo page and open overlays in several theme combinations; reflow at 320 CSS
px; accessibility matrix ([accessibility.md](accessibility.md)).

Not done: manual screen-reader testing (NVDA, JAWS, VoiceOver, TalkBack), forced-colors and zoom review. Automated
results do not establish WCAG conformance.

## E. Integrations

Done: plain CSS (core), Tailwind CSS v4 `@theme` output (verified by compiling with Tailwind 4.3.4), Bootstrap 5.3 Sass
variables and CSS variables (verified by compiling Bootstrap 5.3.8 with Sass 1.107.1), Fluent UI Blazor design-token
mapping (catalog of commonly used alias tokens).

Gaps: Tailwind v3 (`tailwind.config.js`) is not generated; Bootstrap component colors compiled from Sass do not follow
runtime theme switches (only `--bs-*` variables do); the Fluent catalog is a subset and rejects unknown names.

## Verification (latest run)

| Suite | Result |
|---|---|
| Tokens unit and conformance tests | 198 passed |
| Theming tests | 40 passed |
| Primitives bUnit tests | 74 passed |
| Integration tests (real Tailwind and Bootstrap compiles) | 13 passed |
| CLI tests (including committed sample outputs and rule catalog up to date) | 15 passed |
| Browser tests, Chromium | 70 passed (two consecutive full runs) |
| Browser tests, Firefox | 70 passed (two consecutive full runs) |
| Browser tests, WebKit | 70 passed (two consecutive full runs) |
| `bdt check` on the sample design system | Passed: 0 errors, 0 warnings, 240 contrast checks |
| `dotnet build -c Release` | 0 warnings (warnings are errors) |
| `dotnet pack` | 10 packages (plus symbol packages), no warnings |

## Compatibility

- .NET 10; Blazor Web App (Interactive Server, Interactive WebAssembly, static SSR for theme attributes and styles).
- Browsers: current Chromium, Firefox and WebKit (tested). The Popover API fallback covers older engines without
  automated tests.
- All libraries are marked trimmable; the token, theming and integration libraries are also marked AOT-compatible
  (analyzers enabled, no warnings).

## Recommended next steps

1. Manual assistive-technology review per the checklist in [accessibility.md](accessibility.md).
2. Publish packages (release workflow with signing, SourceLink, changelog) and collect feedback on the preview APIs.
3. Enforce benchmark regression thresholds in CI.
4. Virtualized listbox/tree; additional primitives (submenus, menubar, slider, date picker).
5. Gamut mapping for hex output; Tailwind v3 config output if demand exists.
