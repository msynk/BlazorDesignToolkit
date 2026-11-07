# BlazorDesignToolkit.Theming.Blazor

Blazor adapter for BlazorDesignToolkit theming: `ThemeStyles`, `ThemeInitScript`, `ThemeProvider`, `ThemeScope`,
runtime switching without reloads, system color-scheme and contrast tracking, optional cookie or localStorage persistence,
and startup without a flash of the wrong theme.

```csharp
builder.Services.AddDesignTokenTheming(o => { o.Catalog = catalog; o.Persistence = ThemePersistence.Cookie; });
```

See https://github.com/msynk/BlazorDesignToolkit/blob/main/docs/theming.md
