# BlazorDesignToolkit.Testing

Accessibility assertions for rendered Blazor markup (bUnit or any HTML): duplicate ids, broken id references, accessible
names, roles, ARIA states and values, required owned elements, list structure, focusable content inside aria-hidden,
nested interactive content.

```csharp
AccessibilityAssert.NoViolations(cut.Markup);
```

Complements, but does not replace, axe-core scans in a real browser and manual screen-reader review.
