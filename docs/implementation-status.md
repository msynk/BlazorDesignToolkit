# Implementation status

State of the toolkit as of version `0.1.0-preview.1` (November 2026). "Done" means implemented, covered by automated
tests and exercised by the demo app. What is not done, and why, is listed explicitly; nothing here is a stub presented as
finished.

## Summary

| Area | Status | Packages / projects |
|---|---|---|
| A. Core token engine (DTCG 2025.10) | Done | `BlazorDesignToolkit.Tokens` |
| B. Runtime theming | Done | `BlazorDesignToolkit.Theming`, `BlazorDesignToolkit.Theming.Blazor` |
| C. Headless primitives, tier 1 and tier 2 | Done, plus submenus, menubar, slider, number field, date picker, virtualization | `BlazorDesignToolkit.Primitives` |
| Starter styles | Done | `BlazorDesignToolkit.Primitives.StarterStyles` |
| D. Accessibility and audit tooling | Done (all automatable layers); manual screen-reader review is a release step | `BlazorDesignToolkit.Tokens` (contrast), `BlazorDesignToolkit.Testing`, browser tests |
| E. Integrations | Done: plain CSS, Tailwind 4 and 3, Bootstrap 5.3 (incl. runtime theming of compiled components), all Fluent 2 tokens | `BlazorDesignToolkit.Tokens.Tailwind`, `.Bootstrap`, `.Fluent` |
| CLI | Done | `BlazorDesignToolkit.Cli` (`bdt`) |
| Demo app | Done (Interactive Server and WebAssembly) | `demo/Server` (`BlazorDesignToolkit.Demo.Server`), `demo/Client` (`BlazorDesignToolkit.Demo.Client`) |
| Documentation | Done | `docs/`, package READMEs, `CHANGELOG.md` |
| Benchmarks | Done, with a CI regression gate | `benchmarks/BlazorDesignToolkit.Benchmarks`, [performance.md](performance.md) |
| CI | Done (build, tests, quality gates, benchmark gate, pack, 3-browser matrix) | `.github/workflows/ci.yml` |
| Release | Workflow ready; first publication needs the maintainer's NuGet key and approval | `.github/workflows/release.yml` |

## A. Core token engine

- Source-preserving JSON parser with line/column locations, size and depth limits, cancellation, duplicate-key detection,
  optional comments/trailing commas.
- DTCG model: groups, tokens, `$type` inheritance, `$description`, `$extensions`, `$deprecated`, `$extends` (including
  targets that exist only through another extension, resolved in repeated passes; non-terminating chains are circular),
  `$root`, all 13 types, all 14 color spaces.
- References: curly aliases and JSON Pointer `$ref` (percent-decoded per RFC 6901 §6), including composite sub-values;
  type checking; chains and cycles reported with the full chain.
- Resolver module: sets, modifiers, `resolutionOrder`, inputs, same-document pointers in sources (`#/sets/…`, `$defs`,
  arrays of sources); sandboxed file loader; opt-in HTTPS loader with prefix allow-list, redirect checks, size and time limits.
- Value parsing for every type; validation rules; diagnostics with stable ids, policies, JSON/GitHub/SARIF output.
- Export: DTCG JSON, CSS custom properties, CSS Color 4 gamut mapping wherever sRGB is required (option to clip),
  exporter registry, coverage analysis, token diff.

## B. Runtime theming

- Theme catalog (brands, axes, layers, inheritance, `system` values), composition with overrides, completeness analysis.
- Cascade-diff stylesheet generation verified by a cascade evaluator; hooks for integrations (`Declarations`,
  `ElevateSpecificity`).
- `ThemeManager` with persistence (none, localStorage, cookie); Blazor adapter with no-flash startup, prerender safety,
  system preferences; `ThemeScope` with subtree-only overrides.

## C. Headless primitives

ToggleButton, Checkbox (mixed), Switch, RadioGroup, Tabs, Disclosure, Accordion, Dialog, Popover (with a tested
no-Popover-API fallback), Tooltip, Listbox, Select, Menu (checkbox/radio items, separators, nested submenus), Menubar,
RovingFocusGroup, Combobox, CommandMenu, TreeView, Slider, RangeSlider, NumberField, DatePicker, LiveRegion/LiveAnnouncer,
ToastRegion/ToastService, FormField, FocusTrap, VisuallyHidden, DynamicHeading. Listbox and TreeView virtualize long
lists (tested with 10,000 options and 2,000 tree nodes). Controlled components revert changes their parent does not
accept. Built-in texts are localizable through `PrimitiveStrings`. No portal component: overlays use the browser top layer.

## D. Accessibility and audit tooling

Automated: WCAG 2.2 contrast (AA, optional AAA, non-text, focus indicators) per theme combination; markup audit in every
bUnit test; Playwright keyboard and pointer tests in Server and WebAssembly modes; axe-core scans of every demo page and
open overlays in several themes; accessibility-tree (ARIA snapshot) assertions; forced colors; text spacing (SC 1.4.12);
200% text (SC 1.4.4); 320 CSS px reflow (SC 1.4.10); all in Chromium, Firefox and WebKit (forced colors: Chromium only).
See the matrix in [accessibility.md](accessibility.md).

Not automatable: whether speech output is understandable and announcements are timely. That needs NVDA, JAWS, VoiceOver
and TalkBack, which cannot run in this environment; the per-release checklist in [accessibility.md](accessibility.md)
covers it. Automated results do not establish WCAG conformance.

## E. Integrations

Plain CSS (core); Tailwind CSS 4 `@theme` and Tailwind CSS 3 preset (both verified by compiling with Tailwind 4.3.4 and
3.4.17); Bootstrap 5.3 Sass variables and per-theme runtime variables with a component bridge (derived values verified
against a real Bootstrap 5.3.8 build); all 467 Fluent 2 tokens (catalog verified against `@fluentui/tokens`).

## Verification (latest run)

| Suite | Result |
|---|---|
| Tokens unit and conformance tests | 244 passed |
| Theming tests | 42 passed |
| Primitives bUnit tests | 93 passed, 15 consecutive runs each in Debug and Release |
| Integration tests (real Tailwind 4/3 and Bootstrap compiles, Fluent package check) | 31 passed |
| CLI tests | 16 passed |
| Browser tests, Chromium / Firefox / WebKit | 123 / 122 / 122 passed, two consecutive full runs each (1 skipped outside Chromium: forced-colors emulation) |
| Benchmark regression gate | Passed against `benchmarks/baseline.json` (18 benchmarks) |
| `bdt check` on the sample design system | Passed: 0 errors, 0 warnings, 240/240 contrast checks; all 8 configured outputs up to date |
| `dotnet build -c Release` | 0 warnings (warnings are errors) |
| `dotnet pack` | 10 packages (plus symbol packages), no warnings |

## Remaining limitations (by design or outside the toolkit)

- **Manual assistive-technology review** — required per release; see above.
- **Publishing** — the release workflow is ready, but pushing to nuget.org requires the maintainer's `NUGET_API_KEY`
  secret and a `nuget` environment with reviewers; no package has been published.
- **.NET 10 only** — .NET 8 and 9 reach end of support in November 2026, so older targets are not offered.
- **Bootstrap** — SVG icons that Bootstrap embeds as data URIs keep their compiled colors.
- **Popover fallback** — in engines without the Popover API there is no top layer, so clipping ancestors can clip popups.
- **Forced colors** — automated in Chromium's emulation only; other engines and real high-contrast themes are part of the
  manual review.

## Recommended next steps

1. Run the manual assistive-technology review from [accessibility.md](accessibility.md).
2. Configure `NUGET_API_KEY` and the `nuget` environment, then push a `v0.1.0-preview.1` tag to publish.
3. Gather feedback on the preview APIs before 1.0 (the controlled/uncontrolled semantics and `PrimitiveStrings` in
   particular), then add a public API baseline to package validation.
