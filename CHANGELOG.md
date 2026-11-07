# Changelog

All notable changes to this project are documented here. The project follows [Semantic Versioning](https://semver.org/);
while the version is `0.x`, minor versions may contain breaking changes.

## 0.1.0-preview.1 (unreleased)

First preview. See [docs/implementation-status.md](docs/implementation-status.md) for scope, verification results and
known gaps.

### Added

- `BlazorDesignToolkit.Tokens`: DTCG 2025.10 reader/writer with source locations and input limits, alias and JSON Pointer
  resolution, resolver module (sets, modifiers), typed values for all token types and color spaces, validation rules
  with stable diagnostic ids and policies, WCAG 2.2 contrast checks, CSS and DTCG JSON exporters, coverage analysis and
  token diff.
- `BlazorDesignToolkit.Theming`: theme catalog with brands and axes (mode, contrast, density, custom), layer composition,
  `system` values, runtime overrides, cascade-diff stylesheet generation, `ThemeManager` with persistence abstraction.
- `BlazorDesignToolkit.Theming.Blazor`: `ThemeStyles`, `ThemeInitScript`, `ThemeProvider`, `ThemeScope`, localStorage and
  cookie persistence, prerender-safe root attributes.
- `BlazorDesignToolkit.Primitives`: headless, accessible components — toggle button, checkbox, switch, radio group, tabs,
  disclosure, accordion, dialog, popover, tooltip, listbox, select, menu, roving focus group, combobox, command menu,
  tree view, live region, toasts, form field, focus trap, visually hidden, dynamic heading.
- `BlazorDesignToolkit.Primitives.StarterStyles`: optional token-driven stylesheet for the primitives.
- `BlazorDesignToolkit.Testing`: markup accessibility audit for bUnit tests.
- `BlazorDesignToolkit.Tokens.Tailwind`, `.Bootstrap`, `.Fluent`: integration exporters.
- `bdt` CLI: `validate`, `build`, `export`, `coverage`, `diff`, `check`, `rules`; text, JSON, GitHub and SARIF reports.
- Tokens: `$extends` to groups that exist only through another extension; percent-decoded JSON Pointer fragments; CSS Color 4
  gamut mapping for sRGB outputs (`gamutMapping`); opt-in remote sources (`HttpResolverSourceLoader`, CLI `remote`) with an
  HTTPS allow-list; same-document pointers in resolver sources.
- Theming: `ThemeScope.Overrides`; `ThemeCssOptions.Declarations` and `ElevateSpecificity` for integrations.
- Primitives: submenus (`MenuSub`), `Menubar`, `Slider`, `RangeSlider`, `NumberField`, `DatePicker`; virtualization for
  `Listbox` and `TreeView`; Page Up/Page Down in lists and trees; `PrimitiveStrings` for localizable texts; controlled
  components revert changes the parent does not accept; a dialog with `CloseOnEscape=false` survives browser close watchers;
  the no-Popover-API fallback hides closed popups.
- Integrations: Tailwind CSS 3 preset (`tailwind-v3`); Bootstrap runtime variables per theme and mode with a component
  bridge (`bootstrap-themes`); all 467 Fluent 2 tokens.
- Tooling: benchmark regression gate in CI; tag-triggered release workflow (SourceLink, package validation, GitHub
  release, NuGet push behind an approval environment).

### Fixed

- Characters typed quickly into the combobox and command menu could be lost under Interactive Server.
- Decorative glyphs of the starter styles were read as part of accessible names.
- `bdt check` reported `failOn: none` in JSON reports of passing runs.
- Tailwind v4 breakpoints and container sizes used `var()`, which media and container queries do not accept.
- Overlapping value changes (rapid pointer moves) could let an older change revert a newer one in controlled components.
