# Token naming and layers

The engine accepts any valid DTCG naming; the conventions below are recommendations, enforced only when you configure the
corresponding rules (see [validation.md](validation.md)).

## Three layers

1. **Primitive tokens** — raw values with no meaning: palette steps, the spacing scale, radii, font stacks, durations,
   easing curves. `color.blue.600`, `space.4`, `radius.md`.
2. **Semantic tokens** — purpose: `color.text.default`, `color.surface.raised`, `color.border.default`,
   `color.focus.ring`, `shape.radius.control`. They alias primitives, so a theme can change meaning in one place.
3. **Component tokens** (only when needed) — `component.button.primary.background`. They alias semantic tokens.

Configure the layers to get warnings (BDT4006) when a semantic token holds a raw value, a component token skips the
semantic layer, or a primitive references a higher layer:

```json
"layers": {
  "primitive": ["color.neutral.**", "color.blue.**", "space.*", "radius.*"],
  "semantic":  ["color.text.*", "color.surface.*", "color.on-surface.*", "shape.**"],
  "component": ["component.**"]
}
```

## Recommended categories

| Category | Examples | Type |
|---|---|---|
| Palette | `color.neutral.50 … 950`, `color.blue.50 … 950` | color |
| Semantic color | `color.text.{default,muted,inverse,link,danger}`, `color.surface.{default,raised,sunken,accent,danger}`, `color.on-surface.*`, `color.border.{subtle,default}`, `color.focus.ring`, `color.overlay.scrim` | color (aliases) |
| Typography | `font.family.{sans,mono}`, `font.weight.*`, `font.size.{xs…3xl}`, `font.line-height.*`, `font.letter-spacing.*`, `typography.{body,heading,code}` | fontFamily, fontWeight, dimension, number, typography |
| Spacing and sizing | `space.{0…16}`, `size.control.height`, `size.control.padding-inline` | dimension |
| Shape | `radius.{none,sm,md,lg,full}`, `shape.radius.{control,surface}`, `border.width.*`, `stroke.width.*` | dimension |
| Elevation | `shadow.{raised,overlay}` | shadow |
| Motion | `duration.{instant,fast,normal,slow}`, `easing.{standard,decelerate,accelerate}`, `transition.default` | duration, cubicBezier, transition |
| Opacity and layering | `opacity.disabled`, `layer.{dropdown,overlay,modal,toast}` | number |
| Accessibility | `color.focus.ring`, `stroke.width.focus`, `component.focus.offset` | color, dimension |

Pair every background with the foreground meant for it (`color.surface.X` ↔ `color.on-surface.X`) and configure a
pairing rule: missing foregrounds are reported (BDT5004) and every pair is contrast-checked in every theme.

## Scales

Numbered (`100`, `200`, …) or t-shirt (`xs`, `sm`, `md`, `lg`, `xl`, `2xl`) sibling names form a scale. The scale rule
(BDT4004) reports steps that break the scale's direction (by size for dimensions, by duration, weight, or OKLCH lightness
for color ramps).

## Names

- Use kebab-case segments (`on-surface`, `line-height`) or configure another case; numeric segments are always allowed.
- Avoid names that differ only by case (BDT2012); several outputs (CSS custom properties, file systems) are case-insensitive.
- CSS names are derived from paths (`color.text.default` → `--ds-color-text-default`); `$root` segments are omitted;
  collisions are errors (BDT7002).

## Deprecation

Mark tokens with `"$deprecated": "Use color.text.muted"`. References to deprecated tokens are warned (BDT4005); deprecated
tokens are listed (BDT4008) and appear as comments in CSS output when comments are enabled. `bdt diff` reports
deprecations and breaking changes between versions.
