# Performance

Baselines measured with BenchmarkDotNet, plus runtime theme-switch latency measured in a real browser. No regression
thresholds are enforced in CI yet; use these numbers to compare before and after a change on the same machine.

## Running the benchmarks

```bash
dotnet run -c Release --project benchmarks/BlazorDesignToolkit.Benchmarks -- --filter '*'
dotnet run -c Release --project benchmarks/BlazorDesignToolkit.Benchmarks -- --filter '*TokenSet*'   # one class
```

Results (Markdown, CSV, HTML) are written to `BenchmarkDotNet.Artifacts/` (use `--artifacts <dir>` to change it). The
benchmarks use BenchmarkDotNet's `ShortRun` job (3 warm-up and 3 measured iterations), which is quick and good enough to
spot large regressions; drop `[ShortRunJob]` for publishable numbers.

### What is measured

- **`TokenSetBenchmarks`** — a synthetic DTCG document with *N* tokens (100, 2,000, 20,000): one quarter palette colors,
  one quarter semantic aliases, one quarter second-level aliases (so half of the colors resolve through a two-step
  chain), one eighth dimensions and one eighth typography composites whose `fontSize` is an alias.
  - Parse JSON + build model (`DtcgReader.Read`)
  - Resolve references + validate values (`TokenResolver.Resolve`)
  - Design-system rules (`TokenValidator.Validate`, including duplicate-value detection)
  - Export CSS (`CssExporter.Export`, resolved values)
  - Write DTCG JSON (`DtcgWriter.Write`)
- **`ThemeBenchmarks`** — the sample design system in `samples/tokens` (about 200 exported tokens per theme; brands Aurora and
  Ember; axes mode × contrast × density, 8 combinations per brand).
  - Compose one theme selection without the catalog's cache (`ThemeComposer.Compose`)
  - Generate the full theme stylesheet (`ThemeStylesheetGenerator.Generate`, 2 brands × 8 combinations)
  - Compute the declarations of a runtime override (`ThemeStylesheetGenerator.GetOverrideDeclarations`)

## Baseline (November 2026)

Machine: Intel Core i9-14900K, Windows 11 24H2, .NET 10.0.14 (x64 RyuJIT), BenchmarkDotNet 0.15.8, `ShortRun`.
Mean time per operation and managed allocations.

### Token pipeline

| Operation | 100 tokens | 2,000 tokens | 20,000 tokens |
|---|---|---|---|
| Parse JSON + build model | 0.12 ms / 272 KB | 4.6 ms / 5.3 MB | 82 ms / 53 MB |
| Resolve references + validate values | 0.08 ms / 181 KB | 7.5 ms / 3.5 MB | 60 ms / 35 MB |
| Design-system rules | 0.03 ms / 95 KB | 0.9 ms / 2.4 MB | 16 ms / 29 MB |
| Export CSS (resolved) | 0.07 ms / 195 KB | 2.4 ms / 3.7 MB | 34 ms / 37 MB |
| Write DTCG JSON | 0.09 ms / 357 KB | 7.7 ms / 6.8 MB | 122 ms / 66 MB |

Parsing, resolving, validating and exporting a 2,000-token set takes about 15 ms in total (plus process start-up for
the CLI); 20,000 tokens take about 0.2 s. Cost grows linearly with the number of tokens; the 100-token figures are
dominated by fixed overhead. The ShortRun error margins are wide (see the CSV report); treat differences below about
30 % as noise.

### Theming

| Operation | Time | Allocated |
|---|---|---|
| Compose one theme selection (uncached) | 0.24 ms | 527 KB |
| Generate the full theme stylesheet (2 brands × 8 combinations) | 8.0 ms | 13.9 MB |
| Runtime override declarations | 0.48 ms | 1.2 MB |

In an application these costs are paid rarely:

- The theme stylesheet is generated once per process (`ThemeStylesheetCache`, a singleton) and served with the first
  response; it is not regenerated on theme switches.
- `ThemeCatalog.Resolve` caches composed selections, so a selection is composed at most once per process.
- Override declarations are computed only when an override changes (the theme editor), not on a plain switch.

### Runtime theme switch

A switch only writes the theme and axis attributes (and, if overrides are active, inline custom properties) to the root
element in one JavaScript interop call. No CSS is generated or downloaded and the page is not reloaded; the browser
recalculates styles for the changed custom properties.

`ThemingBrowserTests.Theme_switch_latency_is_recorded` measures five mode switches on the demo's Themes page in
Interactive Server mode, from the first click on the select to the moment the new `data-mode` attribute is observed.
That interval includes opening the select, choosing an option, a server round trip on a local connection and Playwright's
polling, so it is an upper bound for the switch itself:

| Browser (Playwright, headless, local server) | Five switches (ms) | Mean |
|---|---|---|
| Chromium | 143, 146, 134, 108, 116 | 129 ms |
| Firefox | 134, 101, 100, 133, 101 | 114 ms |
| WebKit | 369, 398, 356, 349, 326 | 360 ms |

Same machine as above, one run. Most of each interval is spent operating the select and in the server round trip;
the attribute write itself is a single interop call. The WebKit figures are consistently higher in this setup; the
cause was not investigated.

The test fails only if the average exceeds 3 seconds; it guards against pathological regressions, not against small
ones.

## Guidance

- **Token sets.** Thousands of tokens are fine for build-time use. For very large sets prefer the CLI or a build step
  over loading token JSON at application startup; ship the generated CSS instead.
- **Use `$ref`/aliases freely.** Every token is resolved once and memoized, so alias chains do not multiply the cost.
- **Validation.** Contrast checks run per theme combination; with many brands and axes, restrict
  `validation.contrast` pairs to the ones your components actually use.
- **Theme stylesheet size.** The cascade-diff output contains every token once for each brand plus only the changed
  declarations per axis value. Keep axis layers small (override semantic tokens, not the palette) to keep the
  stylesheet small and switches cheap.
- **Runtime overrides.** Each overridden token writes inline custom properties for the token and for every token that
  references it (in resolved mode). Use `References = Preserve` (the default for themes) so that dependents follow via
  `var()` and fewer declarations change.
- **WebAssembly.** In WebAssembly mode the catalog is composed in the browser, which is slower than on the server.
  Load the theme manifest once (a singleton) and keep the default `ThemeCssOptions` so that the stylesheet is generated
  once; the libraries are trimmable.
