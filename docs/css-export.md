# CSS export

```csharp
var tokens = TokenResolver.Resolve(json);
var result = CssExporter.Export(tokens, new CssExportOptions { Prefix = "ds", Layer = "tokens" });
File.WriteAllText("tokens.css", result.Css);   // result.Diagnostics lists anything that could not be exported
```

or `bdt export --to css --set prefix=ds --set layer=tokens -o tokens.css tokens/*.tokens.json`.

## Options

| Option (`--set`) | Default | Effect |
|---|---|---|
| `Prefix` (`prefix`) | `""` | `--{prefix}-color-text-default`. Letters, digits, `-`, `_` only (BDT7006). |
| `NameStyle` (`nameStyle`) | `KebabCase` | `fontSize` → `font-size`; `Preserve` keeps segments and escapes invalid characters. |
| `References` (`references`) | `Resolve` | `Preserve` writes `var(--target)` for aliases and aliased composite parts whose targets are exported. |
| `ColorFormat` (`colorFormat`) | `Auto` | `Auto`: sRGB as hex when 8-bit exact, `rgb(r g b / a)` for exact channels with alpha, otherwise the CSS function of the token's space (`oklch()`, `color(display-p3 …)`). `Hex`: always hex; colors outside sRGB are gamut-mapped (see `GamutMapping`) and reported (BDT7003). `Native`: always the space's CSS function. |
| `GamutMapping` (`gamutMapping`) | `Css` | How hex output brings colors outside sRGB into gamut. `Css`: the CSS Color 4 algorithm (reduce OKLCh chroma, keep lightness and hue; what browsers are specified to do). `Clip`: clamp each channel (can shift hue). |
| `Selector` (`selector`) | `:root` | Rule selector. |
| `Layer` (`layer`) | none | Wraps the output in `@layer`. |
| `ExpandComposites` (`expandComposites`) | `true` | Also writes `-font-family`, `-font-size`, … for typography, `-width/-style/-color` for borders, `-duration/-delay/-timing-function` for transitions. |
| `IncludeComments` (`comments`) | `false` | Descriptions and deprecation notes as comments. |
| `Ordering` (`ordering`) | `Document` | Or `Alphabetical`. |
| `Precision` (`precision`) | `5` | Fraction digits. |
| `Filter` | all | Code only: choose tokens to export. |

Theme stylesheets (`theme-css` output, `ThemeStylesheetGenerator`) add `themeAttribute`, `axisAttributePrefix`,
`systemMediaQueries` and `colorScheme` settings; see [theming.md](theming.md).

## Value mapping

| Type | CSS |
|---|---|
| color | see `ColorFormat` |
| dimension, duration | `16px`, `1rem`, `200ms`, `1.5s` |
| number, fontWeight | `1.5`, `600` |
| fontFamily | `"Inter", "Segoe UI", system-ui, sans-serif` — generic families unquoted, names quoted and escaped |
| cubicBezier | `cubic-bezier(0.2, 0, 0, 1)` |
| strokeStyle | keyword; the object form is reported (BDT7001) and skipped |
| border | `1px solid #303337` (object stroke styles are reported) |
| transition | `200ms cubic-bezier(…) 0ms` (use as `transition: opacity var(--ds-transition-default)`) |
| shadow | `0px 1px 3px 0px rgb(0 0 0 / 0.12)`, layers comma-separated, `inset` first |
| gradient | `#fff 0%, #000 100%` (use as `linear-gradient(to right, var(--ds-gradient-x))`) |
| typography | `font` shorthand (`400 1rem/1.5 "Inter", sans-serif`) plus expanded properties |

## Determinism and safety

- Output is byte-for-byte reproducible: LF line endings, fixed ordering, invariant number formatting, no timestamps.
  `bdt export --check` fails CI when committed outputs are stale (BDT9004).
- Token content is never emitted as raw text. Strings are quoted with CSS escapes for quotes, backslashes, control
  characters and `<`/`>`; comments are sanitized (`*/`, angle brackets), so the CSS can be embedded in an HTML `<style>`
  element. `ThemeStyles` refuses to render CSS containing `</` as defense in depth.
- Collisions between token names that map to the same custom property are errors (BDT7002), never silent overwrites.
