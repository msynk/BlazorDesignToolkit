# Migration and versioning

## From pre-2025.10 DTCG drafts

Older drafts (and many tools) used string values. DTCG 2025.10 uses objects:

| Draft | 2025.10 |
|---|---|
| `"$value": "#ff8800"` | `"$value": { "colorSpace": "srgb", "components": [1, 0.533, 0], "hex": "#ff8800" }` |
| `"$value": "16px"` | `"$value": { "value": 16, "unit": "px" }` |
| `"$value": "200ms"` | `"$value": { "value": 200, "unit": "ms" }` |

1. Enable `"parse": { "allowLegacyValueFormats": true }` in `bdt.config.json` (or `ValueParseOptions.AllowLegacyValueFormats`).
   Legacy values are accepted and each one is reported as BDT2016.
2. Run `bdt build -o migrated.tokens.json --preserve-references`: values are written in the 2025.10 object form while
   aliases are kept.
3. Replace the source files and disable `allowLegacyValueFormats`.

Units other than `px`/`rem` (for example `em`, `%`) are not DTCG 2025.10 dimensions. Prefer `number` tokens for ratios.
If you must keep them, list them in `additionalDimensionUnits`; every use is reported (BDT2014) so the deviation stays visible.

Types outside the thirteen standard types (such as `string` or `boolean`) are rejected (BDT2004). Move such data to
`$extensions`.

## From other token tools

DTCG files exported by design tools and token pipelines can be read directly. Theme sets expressed as separate files per
mode map to a theme manifest (layers with `when`) or to a DTCG resolver document. Keep custom metadata in `$extensions`:
it is preserved by every read/write.

## Versioning of this toolkit

- Packages follow Semantic Versioning. The public API surface of each package is the compatibility contract.
- Diagnostic rule ids are stable; a retired rule's id is never reused for another meaning.
- Generated CSS for the same tokens and options is stable within a minor version; changes to output formatting are
  called out in the release notes because they invalidate `bdt export --check` baselines.
- Breaking changes come with deprecation (`[Obsolete]` with guidance) for at least one minor version where possible.

## Upgrading

Run `bdt diff old.tokens.json new.tokens.json --fail-on-breaking` in pull requests to catch removed or retyped tokens in
your own design system.
