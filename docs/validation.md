# Validation and diagnostics

Every finding the toolkit produces is a `Diagnostic` with:

- a **stable rule id** (`BDT1001`…`BDT9004`) that never changes meaning;
- a **severity** (`error`, `warning`, `info`) that configuration can change or disable (`none`);
- a **message**, the affected **token path** or **component**, and the **source location** (file, line, column) when known;
- **expected** and **actual** values, the **reference chain** for reference problems, and a **suggested fix** when one can be computed;
- an optional **scope** (for example `theme=ember mode=dark contrast=more`) when the check ran for a specific theme combination.

Diagnostics are available as objects (`ValidationReport.Diagnostics`), JSON (`DiagnosticJson`, `bdt … --format json`),
SARIF 2.1.0 (`--format sarif`) and GitHub Actions annotations (`--format github`).

## Configuring rules

```json
{
  "naming": [{ "pattern": "**", "case": "kebab-case" }],
  "requiredTokens": [{ "path": "color.text.default", "type": "color" }],
  "layers": {
    "primitive": ["color.neutral.**", "space.*"],
    "semantic": ["color.text.*", "color.surface.*"],
    "component": ["component.**"]
  },
  "accessibility": {
    "backdrop": "color.surface.default",
    "pairs": [{ "foreground": "color.text.muted", "background": "color.surface.default", "kind": "text" }],
    "pairings": [{ "background": "color.surface.*", "foreground": "color.on-surface.*", "kind": "text" }],
    "focusIndicators": [{ "indicator": "color.focus.ring", "backgrounds": ["color.surface.*"] }],
    "includeAaa": false
  },
  "policy": {
    "failOn": "error",
    "rules": { "BDT4003": "none", "BDT4006": "error" },
    "suppressions": [
      { "rule": "BDT5005", "token": "color.overlay.*", "justification": "Scrim is always rendered over the page surface." }
    ]
  }
}
```

- `naming[].case`: `kebab-case`, `camelCase`, `snake_case` or `PascalCase`; `segmentRegex` and `pathRegex` add custom patterns.
- Token patterns use `*` for one path segment and `**` for any number of segments; `on-*` matches within a segment.
- `kind` is `text` (4.5:1 AA, 7:1 AAA), `large-text` (3:1 / 4.5:1), `non-text` (3:1) or `focus` (3:1).
- Suppressions **must** carry a justification (`BDT9002` otherwise) and are listed separately in every report, so
  they stay reviewable. Unknown rule ids in overrides or suppressions are configuration errors.
- `failOn` sets the lowest severity that fails `bdt validate` / `bdt check` (exit code 1).

Custom rules implement `ITokenValidationRule` and are passed to `TokenValidator.Validate(…, additionalRules: …)`.

## Contrast checks

`ContrastEvaluator` computes WCAG 2.2 relative luminance and contrast ratios in sRGB:

- Colors in any DTCG color space are converted with the CSS Color 4 algorithms. Colors outside the sRGB gamut are
  **indeterminate** (`BDT5005`): what users see depends on the display and on the browser's gamut mapping.
- A translucent foreground is composited over the background. A translucent background is composited over the configured
  `backdrop`; without one the result is indeterminate.
- Components with the `none` keyword are indeterminate.
- Ratios are never rounded up across a threshold (4.499:1 fails 4.5:1; reports show the value truncated to two decimals).
- Failing pairs include a suggested foreground with the same OKLCH hue and chroma and adjusted lightness.

Token validation cannot establish WCAG conformance. Real rendering (images, gradients, overlapping layers, text size),
keyboard behavior and screen-reader output need browser tests and manual review — see [accessibility.md](accessibility.md).

## Rule catalog

Generated with `bdt rules --markdown`. "DTCG requirement" marks rules that enforce a normative requirement of the DTCG
2025.10 format, color or resolver modules; the other rules are product conventions.

| Rule | Title | Category | Default | DTCG requirement | Description |
|---|---|---|---|---|---|
| BDT1001 | Invalid JSON | Parsing | error | yes | The document is not valid RFC 8259 JSON. |
| BDT1002 | Duplicate property name | Parsing | error |  | An object contains the same property name more than once; JSON parsers disagree on which value wins. |
| BDT1003 | Nesting limit exceeded | Parsing | error |  | The document nests deeper than the configured maximum depth. |
| BDT1004 | Input size limit exceeded | Parsing | error |  | The input is larger than the configured maximum size. |
| BDT1005 | Root is not an object | Parsing | error | yes | A design token document must be a JSON object. |
| BDT2001 | Invalid token or group name | Structure | error | yes | Token and group names must not begin with '$' and must not contain '{', '}' or '.'. |
| BDT2002 | Token contains children | Structure | error | yes | An object with $value is a token and must not also contain child tokens or groups. |
| BDT2003 | Unknown reserved property | Structure | warning |  | A '$'-prefixed property is not defined by DTCG 2025.10. It is preserved but has no effect. |
| BDT2004 | Unknown token type | Structure | error | yes | $type must be one of the types defined by DTCG 2025.10 (case-sensitive). |
| BDT2005 | Token type cannot be determined | Structure | error | yes | The token has no $type, is not an alias, and no parent group declares $type. Tools must not guess types. |
| BDT2006 | Invalid token value | Structure | error | yes | The $value does not match the shape required by the token's type. |
| BDT2007 | Unknown value property | Structure | warning |  | A value object contains a property not defined for its type. |
| BDT2008 | Value outside the specified range | Structure | warning |  | A numeric value lies outside the range described by the specification. |
| BDT2009 | Hex fallback differs from components | Structure | warning |  | The color's hex fallback does not match its components after conversion to sRGB. |
| BDT2010 | Invalid reserved property value | Structure | error | yes | A reserved property has the wrong JSON type (for example $description must be a string). |
| BDT2011 | Unsupported construct | Structure | error |  | The input uses a construct this toolkit does not support or that the specification leaves ambiguous. It is reported instead of being reinterpreted. |
| BDT2012 | Names differ only by case | Structure | info |  | Sibling names that differ only by case are valid but easily confused, and may collide in case-insensitive outputs. |
| BDT2013 | Invalid $extends | Structure | error | yes | $extends must reference an existing group. |
| BDT2014 | Non-standard unit | Structure | warning |  | The unit is accepted by configuration but is not defined by DTCG 2025.10; other tools may reject it. |
| BDT2015 | Empty group looks like an incomplete token | Structure | info |  | The group has no children but declares token-like properties; a $value may be missing. |
| BDT2016 | Legacy value format | Structure | warning |  | The value uses a pre-2025.10 draft format (such as a hex color string or a '16px' dimension string). It was accepted because AllowLegacyValueFormats is enabled. |
| BDT3001 | Unresolved reference | References | error | yes | A reference points to a token that does not exist. |
| BDT3002 | Circular reference | References | error | yes | References must not be circular. |
| BDT3003 | Reference targets a group | References | error | yes | Curly-brace references can only target tokens. Use '{group.$root}' to reference a group's root token. |
| BDT3004 | Depends on an invalid token | References | warning |  | The token cannot be resolved because a token it references is invalid. Fix the referenced token first. |
| BDT3005 | Circular group inheritance | References | error | yes | Groups must not create circular $extends chains. |
| BDT3006 | Reference type mismatch | References | error | yes | A reference resolves to a token whose type differs from the type required at that position. |
| BDT3007 | Invalid JSON Pointer reference | References | error | yes | A $ref JSON Pointer is malformed or does not resolve to a location inside a token value. |
| BDT3008 | Reference chain too deep | References | error |  | The reference chain is longer than the configured maximum. |
| BDT3009 | Invalid reference syntax | References | error | yes | A string looks like a reference but is not a valid '{group.token}' reference. |
| BDT4001 | Naming convention violation | DesignSystem | warning |  | A token name does not follow the configured naming convention. |
| BDT4002 | Missing required token | DesignSystem | error |  | A token required by the configured contract is not defined. |
| BDT4003 | Duplicate raw value | DesignSystem | info |  | Several tokens declare the same raw value instead of aliasing a single source token. |
| BDT4004 | Inconsistent scale | DesignSystem | warning |  | Tokens in a numbered scale are not ordered consistently with their names. |
| BDT4005 | Reference to deprecated token | DesignSystem | warning |  | A token references a deprecated token. |
| BDT4006 | Token layer violation | DesignSystem | warning |  | A token does not follow the configured primitive / semantic / component layering. |
| BDT4007 | Required token has wrong type | DesignSystem | error |  | A token required by the configured contract exists but has a different type. |
| BDT4008 | Deprecated token | DesignSystem | info |  | The token is marked $deprecated. |
| BDT5001 | Insufficient text contrast | Accessibility | error |  | Text color and background do not meet WCAG 2.2 SC 1.4.3 Contrast (Minimum): 4.5:1, or 3:1 for large text. |
| BDT5002 | Insufficient non-text contrast | Accessibility | error |  | UI component or graphical object colors do not meet WCAG 2.2 SC 1.4.11 Non-text Contrast (3:1). |
| BDT5003 | Insufficient focus indicator contrast | Accessibility | error |  | The focus indicator color does not reach 3:1 against an adjacent background (WCAG 2.2 SC 1.4.11; see also SC 2.4.13). |
| BDT5004 | Missing foreground/background pairing | Accessibility | warning |  | A background token has no matching foreground token under the configured pairing convention. |
| BDT5005 | Contrast cannot be determined | Accessibility | warning |  | The rendered colors cannot be established reliably (translucent background without backdrop, out-of-gamut color, or 'none' components), so no pass/fail verdict is given. |
| BDT5006 | Enhanced text contrast not met (AAA) | Accessibility | warning |  | Text does not meet WCAG 2.2 SC 1.4.6 Contrast (Enhanced): 7:1, or 4.5:1 for large text. Optional AAA rule. |
| BDT5007 | Invalid contrast pair configuration | Accessibility | error |  | A configured contrast pair references a missing, invalid or non-color token. |
| BDT6001 | Override introduces a new token | Themes | warning |  | A theme layer defines a token that does not exist in the base layers; overrides are expected to replace existing tokens. |
| BDT6002 | Override changes token type | Themes | error |  | A theme layer overrides a token with a value of a different type. |
| BDT6003 | Theme not found | Themes | error |  | A referenced theme is not registered. |
| BDT6004 | Circular theme inheritance | Themes | error |  | Themes must not inherit from themselves directly or indirectly. |
| BDT6005 | Incomplete theme context | Themes | warning |  | Contexts of the same theme axis define different sets of tokens, so switching contexts leaves some tokens unchanged. |
| BDT6006 | Invalid resolver document | Themes | error | yes | The DTCG resolver document violates the resolver module. |
| BDT6007 | Invalid resolver input | Themes | error | yes | Resolver inputs must name declared modifiers and valid contexts, and every modifier without a default must receive an input. |
| BDT6008 | Modifier with a single context | Themes | warning | yes | A resolver modifier should declare two or more contexts. |
| BDT6009 | Duplicate theme | Themes | error |  | Two themes share the same identifier. |
| BDT6010 | Invalid theme selection | Themes | error |  | A selected axis value is not declared by the theme. |
| BDT7001 | Unsupported conversion | Export | warning |  | The token cannot be represented in the export target and was skipped. |
| BDT7002 | Output name collision | Export | error |  | Two tokens map to the same output name. |
| BDT7003 | Color outside target gamut | Export | warning |  | The color cannot be represented exactly in the requested output format and was gamut-clipped. |
| BDT7004 | Invalid integration mapping | Export | error |  | An integration mapping references an unknown token or unknown target variable. |
| BDT7005 | Integration mapping type mismatch | Export | error |  | A token mapped to an integration variable does not have the type that variable requires. |
| BDT7006 | Invalid identifier | Export | error |  | A configured prefix, theme identifier or attribute name is not a valid identifier for the target. |
| BDT8001 | Unused token | Coverage | info |  | The token is not referenced by other tokens and no usage was found in the analysed sources. |
| BDT8002 | Undefined custom property | Coverage | warning |  | A source uses a CSS custom property with the token prefix that no token produces. |
| BDT8003 | Missing component contract token | Coverage | error |  | A component manifest requires a token that is not defined. |
| BDT8004 | Unknown manifest token | Coverage | warning |  | A component manifest lists an optional token that is not defined. |
| BDT8005 | Analysis limitation | Coverage | info |  | Part of the source could not be analysed statically (for example dynamically built custom-property names). |
| BDT8006 | Undeclared token usage | Coverage | info |  | A component's sources use a token that its manifest does not declare. |
| BDT9001 | Invalid configuration | Configuration | error |  | The configuration file is invalid. |
| BDT9002 | Suppression without justification | Configuration | error |  | Every suppression must state a justification. |
| BDT9003 | File not found | Configuration | error |  | An input file or glob did not match any file. |
| BDT9004 | Generated output out of date | Configuration | error |  | A generated output on disk differs from what the current tokens produce. |
