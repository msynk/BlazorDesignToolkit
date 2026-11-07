# Framework integrations

Each integration is a separate package that depends only on `BlazorDesignToolkit.Tokens`. Mappings are type-checked:
a token that does not fit the target is reported (BDT7004 unknown target/token, BDT7005 type mismatch, BDT7001
unsupported conversion) instead of being converted silently. In **reference** mode outputs point at the toolkit's runtime
custom properties, so brand and mode switching keep working; **literal** mode emits values from one resolved token set.

## Tailwind CSS v4 (`BlazorDesignToolkit.Tokens.Tailwind`)

- **Supported:** Tailwind CSS 4.0+ (CSS-first `@theme`). Verified by integration tests that compile the output with
  `@tailwindcss/cli` 4.3.4. Tailwind 3 JavaScript configuration is not generated.
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
- **Limitations:** reference mode requires a prefix (otherwise `--color-x: var(--color-x)` would be circular — reported as
  a configuration error). `reset` clears Tailwind's defaults per mapped namespace (`--color-*: initial`).

## Bootstrap 5.3 (`BlazorDesignToolkit.Tokens.Bootstrap`)

- **Supported:** Bootstrap 5.3. Sass output verified by compiling Bootstrap 5.3.8 with Dart Sass 1.107.
- **`bootstrap-scss`:** Sass variables to import *before* Bootstrap:

```scss
@import "aurora.bootstrap";      // $primary: #2d6dca; $body-bg: #ffffff; …
@import "bootstrap/scss/bootstrap";
```

  Colors must be opaque sRGB because Bootstrap's `tint-color`, `shade-color` and `mix` run at compile time; wide-gamut or
  translucent colors are reported.
- **`bootstrap-css`:** runtime `--bs-*` overrides (reference or literal) with `-rgb` companions (`--bs-primary-rgb: 45, 109, 202`)
  computed from the resolved token. The `-rgb` values are static; generate one file per theme/mode with `selector`
  (for example `[data-bs-theme=dark]`).
- **Limitation (important):** component classes such as `.btn-primary` compile their colors from Sass. Runtime overrides
  change utilities, body colors, links, borders and radii, but not those compiled component colors. Use the Sass output for
  a brand, and runtime variables for mode switching of the variables Bootstrap reads at runtime. Variables Bootstrap only
  reads in Sass are rejected for the CSS output.
- **Known variables:** theme colors, base colors and grays, body/secondary/tertiary/emphasis colors, border, link,
  focus ring, radii, spacing, font families/sizes/weights, line heights and shadows (see `BootstrapVariables.All`).

## Fluent 2 (`BlazorDesignToolkit.Tokens.Fluent`)

- **Supported:** Fluent 2 alias tokens as CSS custom properties (`--colorNeutralForeground1`, `--colorBrandBackground`,
  `--borderRadiusMedium`, `--fontFamilyBase`, `--durationNormal`, `--curveEasyEase`, `--shadow4`, spacing, stroke widths,
  font sizes and line heights), as used by Fluent UI Web Components 3 and the Fluent UI Blazor library built on them.
- **Setup:** load the generated CSS after any theme Fluent applies, or do not apply a Fluent theme.
- **Validation:** names must exist in `FluentTokenCatalog` (case-sensitive, with suggestions). Fluent line heights are
  lengths; a DTCG `number` line-height multiplier is rejected. Durations are written in milliseconds in literal mode.
- **Limitation:** the catalog covers the commonly mapped alias tokens, not every Fluent token; unknown names are rejected
  rather than guessed.

## Plain CSS and custom design systems

The core CSS exporter needs no framework. The demo's "Aurora" design system shows the pattern: components with scoped
CSS that only reference `--ds-*` tokens, a component manifest declaring the tokens each component needs, and coverage
analysis verifying both.

## Writing an exporter

Implement `ITokenExporter` (`Format`, `Description`, `SupportedSettings`, `Export(TokenExportContext)`), keep it
deterministic and free of I/O, and register it in a `TokenExporterRegistry`. Structured configuration from
`bdt.config.json` arrives in `TokenExportContext.Configuration`.
