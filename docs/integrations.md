# Framework integrations

Each integration is a separate package that depends on `BlazorDesignToolkit.Tokens` (Bootstrap also on
`BlazorDesignToolkit.Theming`, for its per-theme output). Mappings are type-checked:
a token that does not fit the target is reported (BDT7004 unknown target/token, BDT7005 type mismatch, BDT7001
unsupported conversion) instead of being converted silently. In **reference** mode outputs point at the toolkit's runtime
custom properties, so brand and mode switching keep working; **literal** mode emits values from one resolved token set.

## Tailwind CSS (`BlazorDesignToolkit.Tokens.Tailwind`)

- **Supported:** Tailwind CSS 4.0+ (CSS-first `@theme`, format `tailwind`) and Tailwind CSS 3 (JavaScript preset, format
  `tailwind-v3`). Verified by integration tests that compile the output with `@tailwindcss/cli` 4.3.4 and Tailwind 3.4.17.
- **Setup:** export, then import after Tailwind:

```css
@import "tailwindcss";
@import "./tokens.tailwind.css";   /* generated */
```

```json
{ "format": "tailwind", "path": "wwwroot/css/tokens.tailwind.css", "settings": { "prefix": "ds" },
  "config": { "mappings": [
    { "tokens": "color.surface.*", "namespace": "color", "stripPrefix": "color" },
    { "tokens": "radius.*", "namespace": "radius" },
    { "tokens": "typography.*", "namespace": "text" }
  ] } }
```

- **Output:** `@theme inline { --color-surface-default: var(--ds-color-surface-default); --radius-md: var(--ds-radius-md); --text-body: var(--ds-typography-body-font-size); --text-body--line-height: …; }`
  → utilities `bg-surface-default`, `rounded-md`, `text-body` follow the active theme.
- **Namespaces and accepted types:** `color` (color), `font` (fontFamily), `text` (dimension or typography, with
  `--line-height`, `--letter-spacing`, `--font-weight` companions), `font-weight`, `tracking`, `leading`, `spacing`,
  `radius`, `shadow`, `inset-shadow`, `drop-shadow` (no spread/inset), `blur`, `perspective`, `breakpoint`, `container`,
  `ease` (cubicBezier).
- **Breakpoints and containers** are always written as literal values: Tailwind uses them inside `@media` and
  `@container` queries, where `var()` is not allowed.
- **Limitations:** reference mode requires a prefix (otherwise `--color-x: var(--color-x)` would be circular — reported as
  a configuration error). `reset` clears Tailwind's defaults per mapped namespace (`--color-*: initial`).

### Tailwind CSS 3 (`tailwind-v3`)

The same mappings (with v4 namespace names) produce a preset module:

```js
// tailwind.config.js
module.exports = { content: ["./**/*.razor"], presets: [require("./tokens.tailwind.cjs")] };
```

```js
// tokens.tailwind.cjs (generated)
module.exports = { "theme": { "extend": {
  "colors": { "surface-default": "color-mix(in oklab, var(--ds-color-surface-default) calc(<alpha-value> * 100%), transparent)" },
  "borderRadius": { "md": "var(--ds-radius-md)" },
  "fontSize": { "body": ["var(--ds-typography-body-font-size)", { "lineHeight": "var(--ds-typography-body-line-height)", … }] } } } };
```

- Namespaces map to v3 theme keys: `color` → `colors`, `font` → `fontFamily`, `text` → `fontSize`, `font-weight`,
  `tracking` → `letterSpacing`, `leading` → `lineHeight`, `spacing`, `radius` → `borderRadius`, `shadow` → `boxShadow`,
  `drop-shadow`, `blur`, `breakpoint` → `screens`, `container` → `maxWidth`, `ease` → `transitionTimingFunction`.
  `inset-shadow` and `perspective` have no Tailwind 3 key and are reported.
- Colors are wrapped in `color-mix()` with Tailwind's `<alpha-value>` placeholder, so opacity modifiers (`bg-surface-default/50`)
  work with runtime variables and any color space. Set `opacityModifiers: false` for plain values.
- Settings: `prefix`, `values` (`reference`/`literal`), `module` (`common-js`/`es-module`), `opacityModifiers`, `extend`
  (`false` replaces Tailwind's defaults), `colorFormat`. Values are JSON-encoded, so token text cannot inject code.

## Bootstrap 5.3 (`BlazorDesignToolkit.Tokens.Bootstrap`)

- **Supported:** Bootstrap 5.3. Sass output verified by compiling Bootstrap 5.3.8 with Dart Sass 1.107.
- **`bootstrap-scss`:** Sass variables to import *before* Bootstrap:

```scss
@import "aurora.bootstrap";      // $primary: #2d6dca; $body-bg: #ffffff; …
@import "bootstrap/scss/bootstrap";
```

  Bootstrap's `tint-color`, `shade-color` and `mix` work in sRGB: wide-gamut colors are CSS Color 4 gamut-mapped (BDT7003)
  and translucent colors are reported.
- **`bootstrap-themes` (recommended for runtime theming):** one stylesheet with Bootstrap's runtime variables for **every
  theme and mode** (same attributes and cascade as the toolkit's theme stylesheet), followed by a **component bridge**. Load
  it after `bootstrap.css`:

```json
{ "format": "bootstrap-themes", "path": "wwwroot/css/aurora.bootstrap.css", "settings": { "prefix": "ds" },
  "config": { "mappings": { "primary": "color.surface.accent", "danger": "color.surface.danger", "body-bg": "color.surface.default" } } }
```

  For each theme color it emits what Bootstrap would compute in Sass for that theme and mode: `--bs-primary` (a `var()`
  reference to the token), `--bs-primary-rgb`, `--bs-primary-text-emphasis`, `-bg-subtle`, `-border-subtle` (light and dark
  variants), and the button states (hover/active backgrounds and borders as `color-mix()` over the runtime color, contrast
  text from Bootstrap's `color-contrast()` algorithm, focus shadow). The bridge points `.btn-*`, `.btn-outline-*`,
  `.text-bg-*`, `.link-*:hover` and the components that use `$component-active-bg` (nav pills, dropdown items, pagination,
  list groups, progress bars, form checks, form focus rings, range thumbs, accordion) at those variables, so theme and mode
  switches recolor them without recompiling Bootstrap. The derived values are verified against a real Bootstrap 5.3.8
  build for dark and light colors, in light and dark mode. Rules use `:is()` so they win over Bootstrap's own `:root` and
  `[data-bs-theme]` rules.
- **`bootstrap-css`:** the same variables and bridge for one token set and selector (`selector`, default
  `:root, [data-bs-theme=light]`), for apps without the toolkit's themes. The dark-mode variants of the emphasis and subtle
  variables follow in a `[data-bs-theme=dark]` block (`darkSelector`; empty to omit), so Bootstrap's dark color mode keeps
  working. For a file that only serves dark mode, set `dark` to `true` and `selector` to a dark selector. A translucent
  theme color gets `--bs-<name>` but no derived variables and no bridge rules (its compiled Bootstrap colors stay).
- **Remaining compile-time parts:** SVG icons that Bootstrap embeds as data URIs with compiled colors (select caret, form
  switch knob, accordion chevron, validation icons) and `$theme-colors` loops for colors you did not map keep their
  compiled values. Variables Bootstrap only reads in Sass are rejected for the runtime outputs.
- **Known variables:** theme colors, base colors and grays, body/secondary/tertiary/emphasis colors, border, link,
  focus ring, radii, spacing, font families/sizes/weights, line heights and shadows (see `BootstrapVariables.All`).

## Fluent 2 (`BlazorDesignToolkit.Tokens.Fluent`)

- **Supported:** all 467 Fluent 2 design tokens as CSS custom properties (`--colorNeutralForeground1`, `--colorBrandBackground`,
  `--borderRadiusMedium`, `--fontFamilyBase`, `--durationNormal`, `--curveEasyEase`, `--shadow4`, `--zIndexPopup`, …), as used
  by Fluent UI Web Components 3 and the Fluent UI Blazor library built on them. The catalog is generated from
  `@fluentui/tokens` (version in `FluentTokenCatalog.SourceVersion`) and an integration test checks it against the package.
- **Setup:** load the generated CSS after any theme Fluent applies, or do not apply a Fluent theme.
- **Validation:** names must exist in `FluentTokenCatalog` (case-sensitive, with suggestions). Fluent line heights are
  lengths; a DTCG `number` line-height multiplier is rejected. Durations are written in milliseconds in literal mode.
- **Types by token family:** `color*` color; `borderRadius*`, `fontSize*`, `lineHeight*`, `spacing*`, `strokeWidth*` dimension;
  `fontFamily*`, `fontWeight*`, `shadow*` (shadow), `duration*`, `curve*` (cubicBezier), `zIndex*` (number). Unknown names are
  rejected rather than guessed.

## Plain CSS and custom design systems

The core CSS exporter needs no framework. The demo's "Aurora" design system shows the pattern: components with scoped
CSS that only reference `--ds-*` tokens, a component manifest declaring the tokens each component needs, and coverage
analysis verifying both.

## Writing an exporter

Implement `ITokenExporter` (`Format`, `Description`, `SupportedSettings`, `Export(TokenExportContext)`), keep it
deterministic and free of I/O, and register it in a `TokenExporterRegistry`. Structured configuration from
`bdt.config.json` arrives in `TokenExportContext.Configuration`.
