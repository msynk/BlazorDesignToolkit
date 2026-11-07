# Architecture

BlazorDesignToolkit is a set of independently usable packages with one-way dependencies. Nothing in the core depends on
Blazor, a browser, or a CSS framework.

```
                      ┌───────────────────────────────┐
                      │ BlazorDesignToolkit.Tokens    │  DTCG model, parser, resolver, values, colors,
                      │ (core engine, .NET only)      │  validation, CSS export, coverage, diff, resolver module
                      └──────────────┬────────────────┘
         ┌─────────────────┬─────────┴───────┬─────────────────────┬───────────────────┐
         ▼                 ▼                 ▼                     ▼                   ▼
 Tokens.Tailwind    Tokens.Bootstrap   Tokens.Fluent      BlazorDesignToolkit.Theming   bdt (CLI)
 (optional)         (optional)         (optional)         (themes, axes, stylesheet,    (uses all of the left)
                                                            ThemeManager; .NET only)
                                                                     │
                                                                     ▼
                                                     BlazorDesignToolkit.Theming.Blazor
                                                     (components + isolated JS module)

 BlazorDesignToolkit.Primitives  ── headless components (Blazor only; no token or theming dependency)
 BlazorDesignToolkit.Primitives.StarterStyles ── optional CSS (no code)
 BlazorDesignToolkit.Testing     ── accessibility assertions for rendered markup (AngleSharp)
```

## A. Core token engine (`BlazorDesignToolkit.Tokens`)

Pipeline:

1. **`SourceJsonParser`** reads UTF-8 JSON with `Utf8JsonReader` into an immutable, order-preserving `SourceNode` tree that
   records line/column for every node. It detects duplicate keys, enforces size and depth limits, supports cancellation and
   never evaluates input.
2. **`DtcgReader`** builds the authored model (`TokenDocument`, `TokenGroup`, `TokenDefinition`) and reports structural
   problems: invalid names, `$value` with children, unknown `$type`, wrongly typed reserved properties. Unknown `$`
   properties and `$extensions` are preserved for round-tripping.
3. **`TokenFlattener`** applies group semantics: `$extends` deep merge (groups merge, tokens are replaced whole), circular
   extension detection, `$type` and `$deprecated` inheritance, `$root` tokens.
4. **`TokenResolver`** resolves curly-brace aliases and `$ref` JSON Pointers at any depth (including composite sub-values
   and color components), determines types (explicit → reference → group), type-checks references against composite
   schemas, detects cycles (reported once, with the full chain), bounds alias depth, and parses values into typed
   `TokenValue`s. Tokens are resolved in ordinal path order so results never depend on document order.
5. Consumers: **`TokenValidator`** (rules), **`CssExporter`** and other `ITokenExporter`s, **`TokenCoverageAnalyzer`**,
   **`TokenDiff`**, **`DtcgWriter`**.

The DTCG resolver module (sets, modifiers, `resolutionOrder`) is implemented by `DtcgResolver` and `ResolverSession`,
with a file loader that cannot leave its root directory or reach the network.

## B. Theming (`BlazorDesignToolkit.Theming`, browser-independent)

- A **`ThemeCatalog`** holds **axes** (color mode, contrast, density, direction, or custom) and **themes** (brands).
  Theme identity is independent of every axis. Themes contain ordered **layers**, each optionally conditioned on axis values,
  and may extend another theme.
- **`ThemeComposer`** merges the matching layers of the inheritance chain, applies runtime overrides last, and resolves
  references *after* merging so semantic aliases pick up overridden primitives. Variant layers that add tokens or change
  types are diagnosed.
- **`ThemeStylesheetGenerator`** writes one stylesheet for every theme × axis combination. Each variant rule only contains
  the declarations that differ from what the cascade of less specific rules already produces (verified exhaustively by
  tests with a CSS cascade evaluator), so runtime switching only changes attributes. `system` values work through
  `@media (prefers-color-scheme)` / `(prefers-contrast)` without JavaScript.
- **`ThemeManager`** holds per-user state (selection, `system` values, overrides, system preferences), batches change
  notifications, and persists through an optional `IThemePreferenceStore`.

## C. Blazor adapter (`BlazorDesignToolkit.Theming.Blazor`)

`ThemeStyles`, `ThemeInitScript`, `ThemeProvider`, `ThemeScope` and `ThemeRootAttributes`. The only JavaScript is the
isolated ES module `bdt-theme.js` (attributes and inline custom properties on the root element, `matchMedia` listeners,
storage/cookie helpers) and the tiny classic `bdt-theme-init.js` for first paint. The adapter never touches Blazor-managed
DOM; it only writes attributes and inline custom properties on the root element.

## D. Headless primitives (`BlazorDesignToolkit.Primitives`)

Semantic HTML first (native `<button>`, `<input>`, `<dialog>`, Popover API). Shared infrastructure:

- `PrimitiveBase` (attribute splatting where state attributes always win, disposal, JS access),
  `PrimitiveContext` (parent/part notifications), `Controllable<T>` (Blazor-style bindable state).
- Pure, unit-tested logic: `KeyboardNavigation` (orientation, RTL, disabled skipping, wrapping), `Typeahead`,
  `OptionList<T>`, `CompositeRegistry<T>` (re-synchronizes item order with the DOM).
- `bdt-primitives.js`: key `preventDefault` driven by a `data-bdt-keys` attribute, dialogs, popover positioning, tooltips,
  focus trapping, hotkeys.

## E. Accessibility and audit tooling

`BlazorDesignToolkit.Testing.AccessibilityAudit` checks rendered markup; browser tests use Playwright and axe-core;
token contrast lives in the core engine. See [accessibility.md](accessibility.md).

## Decisions

| Decision | Rationale |
|---|---|
| Own positional JSON DOM instead of `JsonDocument` | Line/column diagnostics, duplicate-key detection, exact number text, immutable sharing between layers. |
| CSS custom properties exporter lives in the core | It is a web platform feature, not a framework; runtime theming needs it. Framework adapters are separate packages. |
| Theme switching changes attributes, not CSS | One generated stylesheet; switching is a single DOM write with no recomputation in .NET. |
| `system` values via `@media` in CSS | Works before JavaScript loads, during prerendering and with JavaScript disabled. |
| Preference cookie (optional) | Lets the server render the right attributes on the first response — no flash of the wrong theme. |
| Native `<dialog>` and Popover API | Top layer, inertness and light dismiss from the browser, no DOM moves that could conflict with Blazor's renderer; no portal needed. |
| Data-driven listbox/select/combobox/tree | Robust ordering and virtualization-friendly; menus/tabs/accordions are declarative and re-sync order with the DOM. |
| Blazor-style bindable state | Components keep their value and adopt new parameter values, so `@bind-*` and one-way parameters both behave predictably. |
| No runtime dependencies beyond .NET and `Microsoft.AspNetCore.Components.Web` | `System.CommandLine` and `Microsoft.Extensions.FileSystemGlobbing` are used only by the CLI; AngleSharp only by `Testing`. |
| Target framework `net10.0` | Current LTS. .NET 8 and 9 reach end of support in November 2026. |
