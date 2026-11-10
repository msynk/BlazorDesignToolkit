# BlazorDesignToolkit

An interoperable design-token and theming platform for Blazor applications, combined with accessible,
styling-independent UI primitives.

- **Design tokens, interoperable.** Reads and writes the stable [DTCG 2025.10](https://www.designtokens.org/tr/2025.10/)
  format (format, color and resolver modules) — aliases, JSON Pointer references, `$extends`, `$root`, all 13 types and
  all 14 color spaces, CSS Color 4 gamut mapping, opt-in allow-listed remote sources — with diagnostics that point to the
  exact file, line and token.
- **Validated design systems.** Stable rule ids, configurable severities, justified suppressions, naming and layering
  rules, scale checks, and WCAG 2.2 contrast checks in every theme combination that never guess when a result cannot be
  established.
- **Themes that switch instantly.** Brands are independent of color mode, contrast and density. One generated stylesheet
  covers every combination; switching changes an attribute — no reload, no CSS regeneration, no flash on startup, and
  `system` preferences work even without JavaScript.
- **Headless, accessible primitives.** Tabs, dialogs, menus with submenus, menubars, listboxes, selects, comboboxes, tree
  views (both virtualizable), sliders, number fields, date pickers, popovers, tooltips, toasts and more, following the
  WAI-ARIA Authoring Practices, built on native elements, unstyled, with localizable texts.
- **Framework adapters, optional.** Type-checked outputs for Tailwind CSS 4 and 3, Bootstrap 5.3 (including runtime
  theming of its compiled components) and all Fluent 2 tokens — verified by compiling with the real tools.
- **CI tooling.** `bdt validate | build | export | coverage | diff | check` with JSON, SARIF and GitHub annotations.

## Packages

| Package | Purpose | Depends on |
|---|---|---|
| `BlazorDesignToolkit.Tokens` | DTCG engine: parse, resolve, validate, contrast, CSS export, coverage, diff | .NET |
| `BlazorDesignToolkit.Theming` | Theme catalogs, composition, theme stylesheets, `ThemeManager` | Tokens |
| `BlazorDesignToolkit.Theming.Blazor` | Runtime switching, system preferences, persistence, no-flash startup | Theming, Blazor |
| `BlazorDesignToolkit.Primitives` | Accessible headless components | Blazor |
| `BlazorDesignToolkit.Primitives.StarterStyles` | Optional CSS for the primitives | — |
| `BlazorDesignToolkit.Tokens.Tailwind` / `.Fluent` | Framework integrations | Tokens |
| `BlazorDesignToolkit.Tokens.Bootstrap` | Bootstrap integration (Sass, runtime variables per theme, component bridge) | Tokens, Theming |
| `BlazorDesignToolkit.Testing` | Accessibility assertions for component tests | AngleSharp |
| `BlazorDesignToolkit.Cli` (`bdt`) | Command-line tool | all of the above (token side) |

Targets .NET 10 (LTS; .NET 8 and 9 reach end of support in November 2026). Blazor packages support Interactive Server
and Interactive WebAssembly (and Auto).

## Quick start

```csharp
// Tokens only: validate and export
var tokens = TokenResolver.Resolve(File.ReadAllText("tokens/foundation.tokens.json"));
var report = TokenValidator.Validate(tokens);
File.WriteAllText("wwwroot/css/tokens.css", CssExporter.Export(tokens, new() { Prefix = "ds" }).Css);

// Blazor theming
builder.Services.AddDesignTokenTheming(o =>
{
    o.Catalog = (await ThemeManifest.LoadAsync("tokens/app.themes.json")).Catalog!;
    o.Persistence = ThemePersistence.Cookie;
});
```

```razor
<head><ThemeStyles /><ThemeInitScript /></head>
…
<ThemeProvider>
    <Tabs DefaultValue="a">
        <TabList aria-label="Example"><Tab Value="a">A</Tab><Tab Value="b">B</Tab></TabList>
        <TabPanel Value="a">…</TabPanel><TabPanel Value="b">…</TabPanel>
    </Tabs>
</ThemeProvider>
```

```bash
dotnet run --project demo/Server        # interactive demo: guided tour, showcase app, brand builder (?mode=wasm for WebAssembly)
dotnet run --project src/BlazorDesignToolkit.Cli -- check --config demo/bdt.config.json
```

## Documentation

- [Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md)
- [DTCG compatibility](docs/dtcg-compatibility.md) · [Token naming and layers](docs/tokens.md) · [Validation rules](docs/validation.md)
- [Theming and runtime switching](docs/theming.md) · [CSS export](docs/css-export.md) · [Framework integrations](docs/integrations.md)
- [Primitives: API and accessibility contracts](docs/primitives.md) · [Accessibility testing](docs/accessibility.md)
- [CLI](docs/cli.md) · [Performance](docs/performance.md) · [Migration](docs/migration.md) · [Troubleshooting](docs/troubleshooting.md)
- [Implementation status](docs/implementation-status.md)

## Building and testing

```bash
dotnet build BlazorDesignToolkit.slnx
dotnet test tests/BlazorDesignToolkit.Tokens.Tests                      # and the other test projects
npm ci --prefix tests/integration-tools                                  # optional: real Tailwind/Sass compile tests
pwsh tests/BlazorDesignToolkit.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test tests/BlazorDesignToolkit.BrowserTests                      # Playwright + axe-core
BDT_BROWSER=firefox dotnet test tests/BlazorDesignToolkit.BrowserTests  # or webkit (install that browser first)
```

## License

MIT — see [LICENSE](LICENSE).
