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
