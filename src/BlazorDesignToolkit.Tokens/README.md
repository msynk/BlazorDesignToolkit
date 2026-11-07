# BlazorDesignToolkit.Tokens

Framework-independent design-token engine for .NET implementing the DTCG 2025.10 format, color and resolver modules.

- Parse token files with source locations, duplicate-key detection and resource limits.
- Resolve curly-brace aliases and `$ref` JSON Pointers (including inside composite values), `$extends` and `$root`.
- Validate values for all 13 types, design-system rules and WCAG 2.2 contrast in every color space.
- Export deterministic CSS custom properties (with CSS Color 4 gamut mapping where sRGB is required) and DTCG JSON;
  analyse token coverage; diff token sets.
- Load resolver sources from disk (sandboxed) or, opt-in, from allow-listed HTTPS URLs (`HttpResolverSourceLoader`).

```csharp
var tokens = TokenResolver.Resolve(File.ReadAllText("tokens.json"));
var report = TokenValidator.Validate(tokens);
var css = CssExporter.Export(tokens, new CssExportOptions { Prefix = "ds" }).Css;
```

Documentation: https://github.com/msynk/BlazorDesignToolkit/tree/main/docs
