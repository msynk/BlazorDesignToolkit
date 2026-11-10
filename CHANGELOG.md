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
- Demo: an introduction that explains the toolkit with a site-wide theme switcher, a guided tour computed live from the
  sample tokens, a showcase app built only from primitives and tokens, a brand builder (OKLCH palette, contrast in every
  mode, DTCG export), a primitives overview with interchangeable skins, and a live markup inspector on every primitive
  example.
- Demo: a site-wide inspector (pick an element or follow the focus to see each CSS property traced through its tokens to
  the current theme's value, what a screen reader would announce, and a log of live-region announcements), a visual
  style guide generated from the tokens (palettes, semantic colors, measured contrast, type, spacing, shape, elevation,
  states and motion), previous and next page links, and header controls aligned on one row.
- Tooling: benchmark regression gate in CI; tag-triggered release workflow (SourceLink, package validation, GitHub
  release, NuGet push behind an approval environment).

### Fixed

- Characters typed quickly into the combobox and command menu could be lost under Interactive Server.
- Decorative glyphs of the starter styles were read as part of accessible names.
- `bdt check` reported `failOn: none` in JSON reports of passing runs.
- Tailwind v4 breakpoints and container sizes used `var()`, which media and container queries do not accept.
- Overlapping value changes (rapid pointer moves) could let an older change revert a newer one in controlled components.
- Tokens: parsing minified (single-line) JSON was quadratic, because every source location was counted from the start of
  its line. A 1,600-token minified file took over a second (and far longer in WebAssembly); it now takes milliseconds.
- Starter styles: the check mark of checked menu checkbox and radio items pushed the item's label to the end of the row.
- Demo: unknown addresses returned an empty 404 response, and the error page turned into "not found" once the page
  became interactive. Both now render proper pages (`UseStatusCodePagesWithReExecute`, `Router.NotFoundPage`,
  `[ExcludeFromInteractiveRouting]`).
- Demo: the render-mode link in the footer navigated to the home page and kept the current render mode; it now reloads
  the current page in the other mode.
- Demo: pasting a token document larger than 32 KB on the import page disconnected the Interactive Server circuit.
- Demo: the tree view and live editor kept showing values of the previous theme after a theme switch; the editor
  re-resolved and re-validated all tokens several times per render.
- Demo: the date picker example used a fixed "today" (now the real date, or `?today=yyyy-MM-dd`); table text broke
  mid-word; the navigation filled the first screen on phones (now behind a Menu button, with the header on one row down to 320 CSS px); `components.manifest.json` no
  longer reports 22 undeclared token usages; the command menu could not undo "Increase contrast" or "Compact density".

### Demo additions

- "Compare themes" page (token diff between any two theme selections), a live component preview and per-token override
  removal in the live editor, file upload on the import page, a site map on the home and not-found pages, and browser
  tests for each of the fixes above.
