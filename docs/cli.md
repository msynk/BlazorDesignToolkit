# `bdt` command-line tool

Validate, build, export and analyse tokens without starting an application.

```bash
dotnet tool install --global BlazorDesignToolkit.Cli     # or a local tool manifest
bdt --help
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | A quality gate failed: validation at or above `--fail-on`, stale outputs (`--check`), coverage below the minimum, breaking changes (`diff --fail-on-breaking`) |
| 2 | Invalid usage, configuration or input files (unknown options, missing files, invalid `bdt.config.json`, unknown output format) |

## Commands

| Command | Purpose |
|---|---|
| `bdt validate [files…]` | Structure, references, values, design-system rules and contrast. With a theme manifest every theme × axis combination is validated and identical findings are merged, with the affected combinations listed in the scope. |
| `bdt build [files…] [--theme t] [--axis k=v…] [-o file] [--preserve-references]` | Resolve one selection and write self-contained DTCG JSON. |
| `bdt export [--check]` | Generate every configured output, or verify that committed outputs are up to date (`--check`, BDT9004). |
| `bdt export --to <format> [--set k=v…] [-o file] [files…]` | One ad-hoc output (`css`, `theme-css`, `dtcg-json`, `tailwind`, `tailwind-v3`, `bootstrap-scss`, `bootstrap-css`, `bootstrap-themes`, `fluent`). |
| `bdt coverage [--sources glob…] [--prefix p] [--min n]` | Token usage in sources, unused tokens, undefined custom properties, component contracts. |
| `bdt diff before.json after.json [--fail-on-breaking]` | Added, removed, retyped, changed, re-aliased, deprecated tokens. |
| `bdt check` | Every configured gate: validation, `export --check`, coverage. |
| `bdt rules [--markdown] [--format json]` | The rule catalog. |

Common options: `--config <file>` (default `./bdt.config.json` when present), `--themes <manifest>`, `--format text|json|github|sarif`,
`--verbose` (show info diagnostics), `--report <file>` (also write a JSON report), `--fail-on none|info|warning|error`.
Files given on the command line replace the configured token files.

## Configuration (`bdt.config.json`)

All paths are relative to the configuration file.

```json
{
  "tokens": ["tokens/**/*.tokens.json"],
  "themes": "tokens/aurora.themes.json",
  "parse": { "maxInputBytes": 16777216, "maxDepth": 64, "allowComments": false, "allowLegacyValueFormats": false, "additionalDimensionUnits": [] },
  "validation": "tokens/aurora.validation.json",
  "policy": { "failOn": "error", "rules": { "BDT4003": "none" }, "suppressions": [ { "rule": "BDT5005", "token": "color.overlay.*", "justification": "…" } ] },
  "outputs": [
    { "format": "theme-css", "path": "wwwroot/css/tokens.css", "settings": { "prefix": "ds", "layer": "tokens" } },
    { "format": "css", "path": "dist/dark.css", "theme": "aurora", "axes": { "mode": "dark" }, "settings": { "prefix": "ds", "references": "resolve" } },
    { "format": "tailwind", "path": "wwwroot/css/tokens.tailwind.css", "settings": { "prefix": "ds" }, "config": { "mappings": [ { "tokens": "color.surface.*", "namespace": "color", "stripPrefix": "color" } ] } },
    { "format": "bootstrap-themes", "path": "wwwroot/css/tokens.bootstrap.css", "settings": { "prefix": "ds" }, "config": { "mappings": { "primary": "color.surface.accent" } } },
    { "format": "dtcg-json", "path": "dist/tokens.json" }
  ],
  "remote": { "allow": ["https://tokens.example.com/design/"], "timeoutSeconds": 30 },
  "coverage": {
    "sources": ["src/**/*.razor", "src/**/*.css"],
    "exclude": ["**/bin/**", "**/obj/**"],
    "manifests": ["tokens/components.manifest.json"],
    "entryPoints": ["color.**"],
    "prefix": "ds",
    "minimumPercent": 60
  }
}
```

- `tokens` globs are expanded and sorted ordinally before merging, so builds are deterministic. Use either `tokens` or `themes`.
- `validation` is an inline object or a path to a JSON file with the format described in [validation.md](validation.md).
- `policy` here overrides a policy inside the validation file.
- Each output has `format`, `path`, string `settings` (the exporter's documented keys; unknown keys are errors) and optional
  structured `config`, plus `theme`/`axes` to choose the selection for single-set formats.
- `remote` (optional) allows resolver and theme sources to reference `https://` URLs under the listed prefixes. Without it
  the CLI never accesses the network. Prefixes must be absolute HTTPS URLs without query or fragment; redirects are
  followed only to allow-listed URLs; downloads are limited by `parse.maxInputBytes` and `timeoutSeconds`.
- Unknown properties anywhere are reported (BDT9001), so a typo never disables a gate silently.

### Component manifest

```json
{ "components": [ { "name": "AuroraButton", "sources": ["**/AuroraButton.razor*"],
    "requiredTokens": ["component.button.primary.background"], "tokens": ["typography.body"] } ] }
```

Missing required tokens are errors (BDT8003); usage of tokens a manifest does not declare is reported (BDT8006).

## CI

```yaml
- run: dotnet tool restore
- run: dotnet bdt check --config design/bdt.config.json --format github
- run: dotnet bdt validate --config design/bdt.config.json --format sarif > bdt.sarif
- uses: github/codeql-action/upload-sarif@v3
  with: { sarif_file: bdt.sarif }
```

`--format github` prints workflow annotations; `--format sarif` integrates with code scanning; `--report` writes the JSON
report as an artifact.
