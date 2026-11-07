# DTCG 2025.10 compatibility

The toolkit implements the stable Design Tokens Community Group specification, version 2025.10
([format](https://www.designtokens.org/tr/2025.10/format/), [color](https://www.designtokens.org/tr/2025.10/color/),
[resolver](https://www.designtokens.org/tr/2025.10/resolver/)). It does not define a competing format. Product conventions
(naming rules, layering, contrast pairs) are configured separately and never change how a token document is read.

Conformance fixtures adapted from the specification's examples live in
`tests/BlazorDesignToolkit.Tokens.Tests/Fixtures/conformance` (comments and trailing commas in the printed examples were
removed because those examples are not valid JSON).

## Format module

| Feature | Status | Notes |
|---|---|---|
| Media types / `.tokens`, `.tokens.json` | Supported | Files are read regardless of extension. |
| Groups, nested groups, empty groups | Supported | An empty group with `$type`/`$description` gets an informational hint (BDT2015). |
| `$value`, `$type`, `$description`, `$extensions`, `$deprecated` | Supported | `$deprecated: false` overrides a deprecated group. |
| Unknown `$` properties | Preserved, warned (BDT2003) | Never interpreted. `$schema` at the root is accepted silently. |
| `$extensions` preservation | Supported | Round-trips verbatim, including through `DtcgWriter` and `bdt build`. |
| Name rules (`$`, `{`, `}`, `.`) | Enforced (BDT2001) | Names are case-sensitive; names differing only by case get BDT2012 (info). |
| `$value` together with children | Error (BDT2002) | |
| Group `$type` inheritance | Supported | Precedence: explicit `$type` → referenced token's type → nearest group `$type`. |
| Undeterminable type | Error (BDT2005) | Types are never guessed from values. |
| `$root` tokens | Supported | Referenced as `{group.$root}`; referencing the group itself is BDT3003 with a hint. |
| `$extends` (curly or `#/` pointer) | Supported | Groups merge recursively, tokens are replaced whole; circular chains (including extending an ancestor) are BDT3005. |
| `$extends` to a group that exists only through another extension | Supported | Resolved in repeated passes until stable (one pass for documents without such targets). A target that only exists through an extension depending on the extending group is circular (BDT3005). |
| Curly-brace aliases | Supported | Whole-string references only; partial strings such as `"1px {a}"` are BDT3009. |
| Chained aliases, forward references | Supported | Depth limit configurable (`MaxReferenceDepth`, default 64, BDT3008). |
| Circular references | Error (BDT3002) | Reported once with the full chain; dependants get BDT3004. |
| JSON Pointer `$ref` in values and sub-values | Supported | `#/a/b/$value`, `#/a/b/$value/components/0`, inside composite properties. Pointers address the flattened token tree (so inherited tokens are reachable). Fragments are percent-decoded first (RFC 6901 §6, UTF-8): `#/brand%20colors` equals `#/brand colors`; a `%` that does not start a valid escape is kept literally. |
| Token-level `$ref` (example 32) | Supported | Round-trips in the same form. |
| `$ref` with other keys in the same object | Error (BDT3007) | |
| Reference type checking | Supported | Whole-token and composite sub-value references must have the expected type (BDT3006). Pointers to sub-values are validated by the value parser. |

### Types

| Type | Status | Notes |
|---|---|---|
| `color` | Supported | See the color module below. |
| `dimension` | Supported | Units `px`, `rem`. Other units only through `AdditionalDimensionUnits`, always with BDT2014. |
| `fontFamily` | Supported | String or non-empty array. |
| `fontWeight` | Supported | Numbers 1–1000 and every keyword of §8.4 (case-sensitive, with suggestions). |
| `duration` | Supported | `ms`, `s`; negative values warn. |
| `cubicBezier` | Supported | x coordinates must be within [0, 1]. |
| `number` | Supported | |
| `strokeStyle` | Supported | Keyword or `{dashArray, lineCap}`. The object form has no CSS custom-property equivalent and is reported by the CSS exporter (BDT7001). |
| `border`, `transition`, `typography` | Supported | Every sub-value required; extra sub-values are invalid (§9.2). |
| `shadow` | Supported | Single object or array; `inset` optional. A reference inside a shadow array that resolves to another array is rejected: §9.6 says references "do not cause array flattening". |
| `gradient` | Supported | Positions outside [0, 1] are clamped with a warning. **Interpretation:** a reference to a gradient token inside a gradient array inserts that gradient's stops (§9.7 allows references to gradient tokens there but does not define the result). |
| Non-standard types (`string`, `boolean`, …) | Rejected (BDT2004) | The specification defines thirteen types; the example using `"string"` is not normative. |
| Legacy draft values (hex strings, `"16px"`, `"200ms"`) | Rejected by default | `AllowLegacyValueFormats` accepts them with BDT2016 to help migration. |

## Color module

| Feature | Status | Notes |
|---|---|---|
| `colorSpace`, `components`, `alpha`, `hex` | Supported | `alpha` defaults to 1; `hex` must be 6-digit. |
| All 14 color spaces | Supported | srgb, srgb-linear, hsl, hwb, lab, lch, oklab, oklch, display-p3, a98-rgb, prophoto-rgb, rec2020, xyz-d65, xyz-d50. |
| `none` components | Supported | Treated as 0 for conversion (CSS behavior); contrast is reported as indeterminate. |
| Component ranges | Checked, warning (BDT2008) | Hue 360 is outside [0, 360). |
| `hex` consistency | Checked, warning (BDT2009) | Components are authoritative. |
| Conversions | CSS Color 4 algorithms | Matrices for linear RGB ↔ XYZ, Bradford D50/D65, Lab/LCh, OKLab/OKLCh; inverses are derived numerically. Verified against the specification's `#ff00ff` vectors. |
| Gamut mapping | Supported | Where sRGB is required (hex output, Bootstrap Sass and `-rgb` companions) colors outside sRGB use the CSS Color 4 gamut mapping algorithm (OKLCh chroma reduction, ΔEOK JND 0.02) and are reported (BDT7003); `gamutMapping: clip` restores per-channel clipping. Native CSS color functions keep wide-gamut values. |

**Specification erratum.** Example 6 of the color module declares `hsl(330 100% 50%)` with `hex: "#ff00ff"`. That HSL value
is `#ff0080`; magenta has hue 300. The toolkit keeps the components and reports BDT2009 (covered by a test).

## Resolver module

| Feature | Status | Notes |
|---|---|---|
| `version` (`2025.10`), `name`, `description`, `$schema`, `$extensions` | Supported | Unknown root keys warn; `$defs` is ignored without error (§4.5). |
| Sets with inline and referenced sources | Supported | Merged in array order; later tokens win; groups merge. |
| Modifiers, `contexts`, `default` | Supported | 0 contexts → error; 1 context → warning; invalid `default` → error. `context` (singular, used in some examples) is read with a warning. |
| `resolutionOrder` with references and inline sets/modifiers | Supported | Inline items need `name` and `type`; duplicate names are errors. |
| Inputs | Supported | Case-insensitive; unknown modifiers, invalid contexts and missing inputs without defaults are all reported together. |
| Alias resolution after merging | Supported | |
| `$ref` to files relative to the document, with fragments | Supported | Loader restricted to a root directory. |
| Remote `$ref` (URLs) | Supported, opt-in | Off by default. `HttpResolverSourceLoader` (CLI: `"remote": { "allow": ["https://…/"] }`) fetches HTTPS URLs under allow-listed prefixes only, re-checks every redirect, rejects credentials in URLs, and limits size (`maxInputBytes`) and time. |
| Same-document `$ref` inside sources | Supported | `#/sets/name` expands the set's sources; any other pointer (for example into `$defs` or into a set's `sources`) is resolved in the resolver document: an array is a list of sources, an object with `$ref` is a reference object, any other object is inline tokens. Cycles detected. |
| Shallow overrides next to `$ref` | Supported | §4.2.2. |

## Writing

`DtcgWriter` writes deterministic JSON (LF, two-space indentation, fixed reserved-property order). Resolved output gives
every token an explicit `$type`; with `PreserveReferences` the authored values (and references) are kept. Numbers keep
their authored scale where possible (`1.50` stays `1.50`); exponent notation is normalized (`1e2` → `100`).
