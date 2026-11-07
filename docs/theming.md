# Theming

## Concepts

- **Theme** (brand): `aurora`, `ember`. A theme can extend another theme.
- **Axes**, independent of the theme: color `mode` (light/dark), `contrast` (standard/more), `density`
  (comfortable/compact), `dir` (ltr/rtl) or any custom axis. Axes with media queries accept the value `system`.
- **Layers**: token trees applied in order. A layer can be conditional (`"when": { "mode": "dark" }`, or several axes at
  once such as `{ "mode": "dark", "contrast": "more" }`).
- **Overrides**: runtime token values (live editing, user customization) applied after every layer.

References are resolved after layers are merged, so `color.text.default → {color.neutral.900}` in light mode and
`→ {color.neutral.50}` in dark mode both resolve against the right palette, and component tokens follow automatically.

## Authoring a theme manifest

```json
{
  "defaultTheme": "aurora",
  "axes": [
    { "name": "mode", "values": ["light", "dark"], "default": "light", "system": { "dark": "(prefers-color-scheme: dark)" } },
    { "name": "contrast", "values": ["standard", "more"], "default": "standard", "system": { "more": "(prefers-contrast: more)" } },
    { "name": "density", "values": ["comfortable", "compact"], "default": "comfortable" }
  ],
  "themes": [
    {
      "id": "aurora", "name": "Aurora", "metadata": { "owner": "Design Systems" },
      "layers": [
        { "source": "foundation.tokens.json" },
        { "source": "semantic.light.tokens.json" },
        { "source": "semantic.dark.tokens.json", "when": { "mode": "dark" } },
        { "source": "contrast.more.dark.tokens.json", "when": { "mode": "dark", "contrast": "more" } },
        { "source": "density.compact.tokens.json", "when": { "density": "compact" } },
        { "source": "components.tokens.json" }
      ]
    },
    { "id": "ember", "extends": "aurora", "layers": [ { "source": "brands/ember.tokens.json" } ] },
    { "id": "spec", "resolver": "aurora.resolver.json" }
  ]
}
```

- Ids and axis values are identifiers (`[A-Za-z][A-Za-z0-9_-]*`); they are used as attribute values and selectors.
- The axis `default` must not have a system media query: it is what `system` falls back to when nothing matches (and
  during prerendering).
- Conditional layers are expected to override existing tokens. Adding new tokens there is reported (BDT6001) unless the
  layer sets `"mayAddTokens": true`; changing a token's type is an error (BDT6002).
- A theme can be a DTCG **resolver document**: sets become unconditional layers and each modifier becomes an axis whose
  values are its contexts.
- `ThemeCompletenessAnalyzer` (run by `bdt validate`) reports tokens that a variant defines but another value of the same
  axis neither defines nor inherits from a base layer (BDT6005).

Programmatic equivalent: `new ThemeCatalogBuilder().AddStandardAxes().AddTheme(new ThemeDefinition("aurora", layers)).Build()`.

## Runtime switching in Blazor

```csharp
// Program.cs (server and/or WebAssembly)
builder.Services.AddDesignTokenTheming(options =>
{
    options.Catalog = catalog;                              // or CatalogFactory
    options.Css = ThemeCssOptions.Default with { Css = ThemeCssOptions.Default.Css with { Prefix = "ds" }, Layer = "tokens" };
    options.Persistence = ThemePersistence.Cookie;          // None (default) | LocalStorage | Cookie
    options.FollowSystemPreferences = true;
});
```

```razor
@* App.razor *@
<html lang="en" @attributes="ThemeRootAttributes.For(Catalog, ThemeRootAttributes.FromCookie(HttpContext.Request.Cookies["bdt-theme"]), Options.Css)">
<head>
    <ThemeStyles />       @* inline stylesheet; or export it with `bdt export` and use <link> *@
    <ThemeInitScript />   @* only renders when persistence is enabled *@
</head>

@* MainLayout.razor (inside the interactive boundary) *@
<ThemeProvider>@Body</ThemeProvider>
```

Use the cascaded `ThemeManager` (or inject it):

```csharp
manager.SetTheme("ember");
manager.SetAxis("mode", "system");
using (manager.BeginBatch()) { manager.SetAxis("contrast", "more"); manager.SetAxis("density", "compact"); } // one notification
manager.SetOverride("color.blue.600", ColorValue.FromHex("#ff0000").ToSourceNode());
await manager.ResetAsync();
var tokens = manager.Resolve().Tokens; // effective tokens, including overrides
```

`Try*` methods return `false` for unknown themes or invalid axis values and leave the state unchanged; `LastDiagnostics`
explains why. A persisted preference naming a theme that no longer exists falls back to the default theme with a
diagnostic.

What happens on a switch: `ThemeProvider` writes the theme and axis attributes (`data-theme`, `data-mode`, …) to the root
element in one interop call. Overrides are written as inline custom properties — only declarations whose values actually
change. No CSS is regenerated and the page is not reloaded. The JavaScript module re-applies its state after enhanced
navigation, which can rewrite attributes of the document element.

`ThemeScope` applies another theme to a subtree (previews, embedded brands) without JavaScript. It renders every axis
attribute, because custom properties set on an element are inherited by its descendants.

## Startup without a flash of the wrong theme

| Situation | What renders first | Recommendation |
|---|---|---|
| No persistence | Default theme; `system` axes resolved by CSS `@media` | Works everywhere, including static SSR and no-JS. |
| Cookie persistence | Server renders the stored attributes on `<html>` | Best: correct on the first byte, even without JavaScript. Requires reading the cookie in `App.razor`. |
| localStorage persistence | `ThemeInitScript` (a synchronous classic script in `<head>`) applies the stored values before the body paints | For static hosting/WebAssembly without a server. Values are validated against the declared themes and axes. |
| Prerendering + interactive | Server/prerender output, then `ThemeProvider` loads the preference and system preferences after the first interactive render | Use cookie persistence to avoid any visible change. |
| Server circuit disconnected | Last applied attributes stay; interop failures are ignored | Nothing to do. |

Privacy: the preference is a short string such as `theme=ember;mode=dark` with no identifier. The cookie is first-party,
`SameSite=Lax`, `Secure` on HTTPS, and not `HttpOnly` because the browser module writes it. No storage is used unless you
enable persistence.

## Generated CSS

```css
@layer tokens {
  :where(:root:not([data-theme]), [data-theme="aurora"]) { color-scheme: light; --ds-color-blue-600: #2d6dca; /* … all tokens */ }
  :where(:root:not([data-theme]), [data-theme="aurora"])[data-mode="dark"] { color-scheme: dark; /* only what changes */ }
  @media (prefers-color-scheme: dark) {
    :where(:root:not([data-theme]), [data-theme="aurora"])[data-mode="system"] { /* same as dark */ }
  }
  :where(…)[data-mode="dark"][data-contrast="more"] { /* only what the two single-axis rules do not already produce */ }
}
```

Theme selectors use `:where()`, so specificity equals the number of axis attributes and your own rules override tokens
easily. References are emitted as `var(--…)` by default so runtime overrides of a primitive cascade to every alias.
See [css-export.md](css-export.md) for options.
