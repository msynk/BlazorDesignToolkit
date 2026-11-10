# Getting started

Requirements: .NET 10 SDK. Blazor packages support Interactive Server, Interactive WebAssembly and Auto render modes;
static server rendering renders correct markup without interactivity.

> Packages are published to NuGet by the release workflow (`.github/workflows/release.yml`) when a version tag is pushed.
> Before the first release, reference the projects in `src/` or build packages with `dotnet pack -c Release -o artifacts`.

## 1. Write tokens (DTCG 2025.10)

`tokens/foundation.tokens.json`:

```json
{
  "color": {
    "$type": "color",
    "blue": { "600": { "$value": { "colorSpace": "srgb", "components": [0.176, 0.427, 0.792], "hex": "#2d6dca" } } },
    "white": { "$value": { "colorSpace": "srgb", "components": [1, 1, 1], "hex": "#ffffff" } },
    "surface": { "default": { "$value": "{color.white}" }, "accent": { "$value": "{color.blue.600}" } },
    "on-surface": { "accent": { "$value": "{color.white}" } }
  },
  "radius": { "md": { "$type": "dimension", "$value": { "value": 0.5, "unit": "rem" } } }
}
```

## 2. Validate and export (no application needed)

```bash
dotnet bdt validate tokens/foundation.tokens.json
dotnet bdt export --to css --set prefix=ds -o wwwroot/css/tokens.css tokens/foundation.tokens.json
```

or from code:

```csharp
var tokens = TokenResolver.Resolve(File.ReadAllText("tokens/foundation.tokens.json"));
var report = TokenValidator.Validate(tokens, new ValidationOptions
{
    Accessibility = new AccessibilityRules { Pairings = [new ContrastPairing("color.surface.*", "color.on-surface.*")] },
});
var css = CssExporter.Export(tokens, new CssExportOptions { Prefix = "ds" }).Css;
```

## 3. Themes and runtime switching

Write a theme manifest (see [theming.md](theming.md)), then:

```csharp
// Program.cs
var catalog = (await ThemeManifest.LoadAsync("tokens/app.themes.json")).Catalog
    ?? throw new InvalidOperationException("Invalid theme manifest");
builder.Services.AddDesignTokenTheming(o =>
{
    o.Catalog = catalog;
    o.Css = ThemeCssOptions.Default with { Css = ThemeCssOptions.Default.Css with { Prefix = "ds" } };
    o.Persistence = ThemePersistence.Cookie;
});
```

```razor
@* App.razor <head> *@
<ThemeStyles />
<ThemeInitScript />

@* MainLayout.razor *@
<ThemeProvider>@Body</ThemeProvider>

@* Anywhere inside: *@
@code { [CascadingParameter] ThemeManager Manager { get; set; } = default!; }
<button @onclick='() => Manager.SetAxis("mode", "dark")'>Dark mode</button>
```

Style with the generated variables: `background: var(--ds-color-surface-default)`.

## 4. Headless primitives

```csharp
builder.Services.AddHeadlessPrimitives(); // only needed for ToastRegion / LiveRegion
```

```razor
@using BlazorDesignToolkit.Primitives

<Menu>
    <MenuTrigger>Actions</MenuTrigger>
    <MenuContent>
        <MenuItem TextValue="Rename" OnSelect="Rename">Rename</MenuItem>
        <MenuItem TextValue="Delete" OnSelect="Delete">Delete</MenuItem>
    </MenuContent>
</Menu>
```

Optional CSS: `<link rel="stylesheet" href="_content/BlazorDesignToolkit.Primitives.StarterStyles/bdt-starter.css" />`,
then map `--bdt-*` to your tokens (`:root { --bdt-accent: var(--ds-color-surface-accent); }`).

## 5. Test accessibility

```csharp
var cut = Render(@<Tabs>…</Tabs>);
AccessibilityAssert.NoViolations(cut.Markup);   // BlazorDesignToolkit.Testing
```

## Adoption paths

| You want | Use |
|---|---|
| Validate/convert tokens in CI only | `bdt` (or `BlazorDesignToolkit.Tokens` in a build task) |
| Token CSS for any web stack | `bdt export --to css` / `theme-css` |
| Runtime brand and mode switching in Blazor | `Theming` + `Theming.Blazor` |
| Accessible behavior under your own design system | `Primitives` (+ `StarterStyles` to start) |
| Tailwind / Bootstrap / Fluent | the matching `Tokens.*` package or `bdt export` format |

## Run the demo

```bash
dotnet run --project demo/Server
```

Where to start:

| Page | What it shows |
|---|---|
| Introduction (`/`) | What the toolkit does, a theme switcher for the whole site, and how the pieces fit together |
| Guided tour (`/tour`) | The Aurora design system through every stage: tokens, references, validation, theme composition, the generated stylesheet, runtime switching, primitives and CI, all computed live |
| Showcase app (`/showcase`) | A small project tracker built only from primitives and tokens, in its own theme scope (brand, mode, density, right to left) |
| Brand builder (`/brand-builder`) | One color becomes an OKLCH palette; previews and contrast checks in every mode; DTCG export; apply to the site |
| Primitives (`/primitives`) | The same components with the starter styles, custom CSS and no CSS at all, and the full catalog |
| Visual style guide (`/style-guide`) | Every token drawn with its CSS variable: palettes, semantic colors, measured contrast, type, spacing, shape, elevation, states and motion |

Every primitive example has a **Live markup** panel that shows its rendered HTML, with roles, ARIA states and
relationships highlighted, updating as you use it.

**Inspect** in the header (or "Inspect an element" in the command menu) opens the inspector on any page: pick an element,
or move the focus to it, to see each CSS property traced through its design tokens to the value of the current theme,
and what a screen reader would announce (role, name, states, position). It also logs live-region announcements.

Append `?mode=wasm` to switch to Interactive WebAssembly (`?mode=server` to switch back), or use the link in the page
footer; the choice is remembered in a cookie. Press <kbd>Ctrl</kbd>+<kbd>K</kbd> for the command menu (pages, brands and
theme axes). The date picker example and the showcase app use today's date; append `?today=2026-11-02` to their pages
to pin it.
