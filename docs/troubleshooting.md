# Troubleshooting and FAQ

**The page flashes the default theme before switching.**
Use cookie persistence and render `ThemeRootAttributes.For(…)` on `<html>` in `App.razor`, or enable `LocalStorage`
persistence together with `<ThemeInitScript />` in `<head>`. `system` values need no JavaScript at all. See
[theming.md](theming.md#startup-without-a-flash-of-the-wrong-theme).

**Theme switching does nothing.**
`ThemeProvider` must be inside an interactive render boundary (for example the layout of interactive `Routes`). During
static rendering, change the theme through the cookie and a reload, or a form post.

**A nested `ThemeScope` shows the wrong mode.**
Custom properties are inherited. `ThemeScope` renders every axis attribute for that reason; custom wrappers must do the same.

**My Tailwind utilities do not change with the theme.**
Generate the Tailwind output in reference mode (default) with a prefix, load the token stylesheet, and make sure the
utilities you use belong to mapped namespaces.

**Bootstrap buttons keep the old color at runtime.**
`.btn-*` colors are compiled from Sass. Use the `bootstrap-scss` output for brand colors; runtime `--bs-*` overrides only
affect what Bootstrap reads at runtime. See [integrations.md](integrations.md#bootstrap-53-blazordesigntoolkittokensbootstrap).

**BDT5005 "Contrast cannot be determined".**
The color is outside sRGB, uses `none`, or the background is translucent without a `backdrop`. Configure
`accessibility.backdrop`, use in-gamut colors, or suppress with a justification after a manual check.

**BDT3003 "Reference targets a group".**
Curly references point at tokens. If the group has a `$root` token, reference `{group.$root}`.

**BDT6001 "Override introduces a new token".**
A conditional layer (for example dark mode) defines a token the base does not. Define it in the base layer, fix the path,
or set `"mayAddTokens": true` on the layer.

**`bdt export --check` fails in CI but not locally.**
Outputs are deterministic, so a difference means the committed file is stale or was edited by hand, or line endings were
converted by Git. Add `*.css text eol=lf` (and other generated files) to `.gitattributes`.

**Keyboard navigation in a menu or tabs follows the wrong order.**
Items are re-synchronized with DOM order before the first keyboard move after any change. If you reorder items with CSS
(`order`, `flex-direction: row-reverse`), DOM order and visual order differ — avoid that (WCAG 1.3.2 Meaningful Sequence).

**Focus jumps to the page heading just after the page becomes interactive.**
That is Blazor's `<FocusOnNavigate Selector="h1">` (used by the demo and the Blazor templates): it focuses the heading
after the first interactive render, even if the user has already focused something in the prerendered page. Browser
tests must wait for it before interacting (see `PageExtensions.OpenAsync` in the browser tests); in an app, remove it or
replace it with a component that only moves focus when focus is still on `<body>`.

**Interop errors after the server circuit disconnects.**
The primitives and theming adapter ignore `JSDisconnectedException`; if you see errors, they come from your own interop.

**Can I use the primitives without the toolkit's tokens?**
Yes. `BlazorDesignToolkit.Primitives` has no dependency on tokens or theming.

**Can I use the token engine outside Blazor?**
Yes. `BlazorDesignToolkit.Tokens` and `BlazorDesignToolkit.Theming` are plain .NET libraries (trimming- and AOT-compatible).

**Does the toolkit send telemetry or access the network?**
No. Resolver file references cannot leave the configured root, and remote references are rejected.
