using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace BlazorDesignToolkit.Testing;

public enum AuditSeverity
{
    Warning,
    Violation,
}

/// <summary>One finding of <see cref="AccessibilityAudit"/>.</summary>
public sealed record AccessibilityIssue(string Rule, AuditSeverity Severity, string Message, string Element)
{
    public override string ToString() => $"{Severity.ToString().ToLowerInvariant()} [{Rule}] {Message}\n    at {Element}";
}

/// <summary>
/// Static accessibility checks on rendered markup. They catch the structural mistakes that unit tests can detect
/// reliably (relationships, names, roles, states). They do not replace axe-core scans in a real browser, keyboard
/// testing, or manual screen-reader review, and they cannot evaluate CSS (contrast, visibility, focus indicators).
/// </summary>
public static class AccessibilityAudit
{
    private static readonly HashSet<string> s_roles = new(StringComparer.Ordinal)
    {
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox", "code", "columnheader",
        "combobox", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory", "document", "emphasis", "feed", "figure",
        "form", "generic", "grid", "gridcell", "group", "heading", "img", "insertion", "link", "list", "listbox", "listitem", "log", "main",
        "marquee", "math", "menu", "menubar", "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option",
        "paragraph", "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search",
        "searchbox", "separator", "slider", "spinbutton", "status", "strong", "subscript", "superscript", "switch", "tab", "table", "tablist",
        "tabpanel", "term", "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem",
    };

    /// <summary>Roles that must have an accessible name (ARIA 1.2 "name required"), plus labelled composite widgets.</summary>
    private static readonly HashSet<string> s_nameRequired = new(StringComparer.Ordinal)
    {
        "button", "checkbox", "combobox", "dialog", "alertdialog", "link", "listbox", "menuitem", "menuitemcheckbox", "menuitemradio", "option",
        "radio", "radiogroup", "searchbox", "slider", "spinbutton", "switch", "tab", "textbox", "tree", "treeitem", "tabpanel", "progressbar", "meter",
    };

    private static readonly HashSet<string> s_nameRecommended = new(StringComparer.Ordinal) { "tablist", "menu", "toolbar", "region", "grid", "table" };

    private static readonly Dictionary<string, string[]> s_requiredContext = new(StringComparer.Ordinal)
    {
        ["tab"] = ["tablist"],
        ["option"] = ["listbox", "group"],
        ["menuitem"] = ["menu", "menubar", "group"],
        ["menuitemcheckbox"] = ["menu", "menubar", "group"],
        ["menuitemradio"] = ["menu", "menubar", "group"],
        ["treeitem"] = ["tree", "group"],
    };

    private static readonly string[] s_booleanAttributes =
        ["aria-expanded", "aria-selected", "aria-disabled", "aria-hidden", "aria-modal", "aria-required", "aria-multiselectable", "aria-atomic", "aria-busy", "aria-readonly", "aria-multiline"];

    private static readonly Dictionary<string, string[]> s_enumeratedAttributes = new(StringComparer.Ordinal)
    {
        ["aria-checked"] = ["true", "false", "mixed"],
        ["aria-pressed"] = ["true", "false", "mixed"],
        ["aria-invalid"] = ["true", "false", "grammar", "spelling"],
        ["aria-orientation"] = ["horizontal", "vertical"],
        ["aria-live"] = ["off", "polite", "assertive"],
        ["aria-haspopup"] = ["true", "false", "menu", "listbox", "tree", "grid", "dialog"],
        ["aria-autocomplete"] = ["none", "inline", "list", "both"],
        ["aria-current"] = ["true", "false", "page", "step", "location", "date", "time"],
        ["aria-sort"] = ["none", "ascending", "descending", "other"],
    };

    private static readonly string[] s_idRefLists = ["aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-flowto", "aria-details"];
    private static readonly string[] s_idRefs = ["aria-activedescendant", "aria-errormessage"];

    public static IReadOnlyList<AccessibilityIssue> Audit(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument("<!DOCTYPE html><html lang=\"en\"><body>" + html + "</body></html>");
        return Audit(document.Body!);
    }

    public static IReadOnlyList<AccessibilityIssue> Audit(IElement root)
    {
        var issues = new List<AccessibilityIssue>();
        var document = root.Owner!;
        var all = root.QuerySelectorAll("*").ToList();

        // Duplicate ids break every id reference.
        foreach (var group in all.Where(static e => e.HasAttribute("id")).GroupBy(static e => e.Id!).Where(static g => g.Count() > 1))
        {
            issues.Add(new("duplicate-id", AuditSeverity.Violation, $"id \"{group.Key}\" is used {group.Count()} times.", Describe(group.First())));
        }

        foreach (var element in all)
        {
            var role = Role(element);
            var explicitRole = element.GetAttribute("role");

            if (explicitRole is not null)
            {
                foreach (var r in explicitRole.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(1))
                {
                    if (!s_roles.Contains(r)) issues.Add(new("invalid-role", AuditSeverity.Violation, $"\"{r}\" is not a WAI-ARIA 1.2 role.", Describe(element)));
                }
            }

            foreach (var attribute in s_idRefLists)
            {
                if (element.GetAttribute(attribute) is not { } value) continue;
                foreach (var id in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (document.GetElementById(id) is null)
                        issues.Add(new("broken-idref", AuditSeverity.Violation, $"{attribute} references \"{id}\", which does not exist.", Describe(element)));
                }
            }
            foreach (var attribute in s_idRefs)
            {
                if (element.GetAttribute(attribute) is not { Length: > 0 } id) continue;
                var target = document.GetElementById(id);
                if (target is null)
                    issues.Add(new("broken-idref", AuditSeverity.Violation, $"{attribute} references \"{id}\", which does not exist.", Describe(element)));
                else if (attribute == "aria-activedescendant" && !element.Contains(target) && !IsControlledBy(element, target, document))
                    issues.Add(new("activedescendant-scope", AuditSeverity.Violation, $"aria-activedescendant target \"{id}\" is neither a descendant nor in an element the widget controls.", Describe(element)));
            }
            if (element.LocalName == "label" && element.GetAttribute("for") is { Length: > 0 } labelFor)
            {
                var target = document.GetElementById(labelFor);
                if (target is null) issues.Add(new("broken-idref", AuditSeverity.Violation, $"label[for] references \"{labelFor}\", which does not exist.", Describe(element)));
                else if (!IsLabelable(target)) issues.Add(new("label-target", AuditSeverity.Violation, $"label[for] references a non-labelable <{target.LocalName}>.", Describe(element)));
            }

            foreach (var attribute in s_booleanAttributes)
            {
                if (element.GetAttribute(attribute) is { } v && v is not ("true" or "false"))
                    issues.Add(new("invalid-aria-value", AuditSeverity.Violation, $"{attribute}=\"{v}\" must be \"true\" or \"false\".", Describe(element)));
            }
            foreach (var (attribute, allowed) in s_enumeratedAttributes)
            {
                if (element.GetAttribute(attribute) is { } v && !allowed.Contains(v))
                    issues.Add(new("invalid-aria-value", AuditSeverity.Violation, $"{attribute}=\"{v}\" must be one of {string.Join(", ", allowed)}.", Describe(element)));
            }
            foreach (var attribute in new[] { "aria-level", "aria-setsize", "aria-posinset" })
            {
                if (element.GetAttribute(attribute) is { } v && (!int.TryParse(v, out var n) || n < (attribute == "aria-setsize" ? -1 : 1)))
                    issues.Add(new("invalid-aria-value", AuditSeverity.Violation, $"{attribute}=\"{v}\" must be a positive integer.", Describe(element)));
            }

            if (role is not null && s_requiredContext.TryGetValue(role, out var contexts))
            {
                var parentRole = ContextRole(element);
                if (parentRole is null || !contexts.Contains(parentRole))
                    issues.Add(new("required-context", AuditSeverity.Violation, $"role=\"{role}\" must be inside {string.Join(" or ", contexts.Select(c => $"role=\"{c}\""))}.", Describe(element)));
            }
            if (role is "tablist" or "listbox" or "tree" or "menu" && !element.QuerySelectorAll("[role]").Any(c => (role, Role(c)) switch
                {
                    ("tablist", "tab") => true,
                    ("listbox", "option") => true,
                    ("tree", "treeitem") => true,
                    ("menu", "menuitem" or "menuitemcheckbox" or "menuitemradio") => true,
                    _ => false,
                }) && !IsHidden(element) && element.GetAttribute("data-state") != "closed")
            {
                issues.Add(new("required-children", AuditSeverity.Warning, $"role=\"{role}\" contains no owned items.", Describe(element)));
            }

            if (role is "checkbox" or "switch" or "menuitemcheckbox" or "menuitemradio" or "radio" && !IsNativeInput(element) && !element.HasAttribute("aria-checked"))
                issues.Add(new("required-state", AuditSeverity.Violation, $"role=\"{role}\" requires aria-checked.", Describe(element)));
            if (role == "combobox" && element.HasAttribute("role") && !element.HasAttribute("aria-expanded"))
                issues.Add(new("required-state", AuditSeverity.Violation, "role=\"combobox\" requires aria-expanded.", Describe(element)));
            if (role is "slider" && !element.HasAttribute("aria-valuenow"))
                issues.Add(new("required-state", AuditSeverity.Violation, "role=\"slider\" requires aria-valuenow.", Describe(element)));

            if (role is not null && !IsHidden(element))
            {
                var needsName = s_nameRequired.Contains(role) || IsNativeControl(element);
                if ((needsName || s_nameRecommended.Contains(role)) && string.IsNullOrWhiteSpace(AccessibleName(element)))
                {
                    issues.Add(new("accessible-name", needsName ? AuditSeverity.Violation : AuditSeverity.Warning,
                        $"<{element.LocalName}> with role \"{role}\" has no accessible name.", Describe(element)));
                }
            }

            if (element.LocalName is "ul" or "ol" && element.GetAttribute("role") is null or "list")
            {
                foreach (var child in element.Children.Where(static c => c.LocalName is not ("script" or "template")))
                {
                    if (child.LocalName != "li" || child.GetAttribute("role") is { } r && r != "listitem")
                        issues.Add(new("list-children", AuditSeverity.Violation, $"<{element.LocalName}> may only contain <li> elements that keep the listitem role.", Describe(child)));
                }
            }

            if (element.GetAttribute("tabindex") is { } tabindex && int.TryParse(tabindex, out var ti) && ti > 0)
                issues.Add(new("positive-tabindex", AuditSeverity.Warning, $"tabindex=\"{ti}\" changes the natural focus order.", Describe(element)));

            if (IsFocusable(element) && element.Ancestors<IElement>().Prepend(element).Any(static a => a.GetAttribute("aria-hidden") == "true"))
                issues.Add(new("focusable-hidden", AuditSeverity.Violation, "A focusable element is inside an aria-hidden subtree.", Describe(element)));

            if (role is "button" or "link" && element.QuerySelectorAll("*").Any(c => c != element && IsInteractive(c)))
                issues.Add(new("nested-interactive", AuditSeverity.Violation, $"Interactive content is nested inside a {role}.", Describe(element)));
        }
        return issues;
    }

    private static bool IsControlledBy(IElement widget, IElement target, IDocument document)
    {
        var controls = (widget.GetAttribute("aria-controls") ?? "") + " " + (widget.GetAttribute("aria-owns") ?? "");
        return controls.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(document.GetElementById).Any(c => c is not null && c.Contains(target));
    }

    /// <summary>The element's explicit role, or its implicit role for common native elements.</summary>
    public static string? Role(IElement element)
    {
        if (element.GetAttribute("role") is { Length: > 0 } explicitRole) return explicitRole.Split(' ')[0];
        return element.LocalName switch
        {
            "button" => "button",
            "a" when element.HasAttribute("href") => "link",
            "input" => element.GetAttribute("type")?.ToLowerInvariant() switch
            {
                "checkbox" => "checkbox",
                "radio" => "radio",
                "range" => "slider",
                "button" or "submit" or "reset" or "image" => "button",
                "hidden" => null,
                "search" => "searchbox",
                _ => "textbox",
            },
            "select" => element.HasAttribute("multiple") ? "listbox" : "combobox",
            "textarea" => "textbox",
            "dialog" => "dialog",
            "nav" => "navigation",
            "main" => "main",
            "ul" or "ol" => "list",
            "li" => "listitem",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
            _ => null,
        };
    }

    private static string? ContextRole(IElement element)
    {
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            var role = parent.GetAttribute("role");
            if (role is "none" or "presentation" || (role is null && Role(parent) is null or "listitem" or "list")) continue;
            return role ?? Role(parent);
        }
        return null;
    }

    private static bool IsNativeInput(IElement e) => e.LocalName == "input";

    private static bool IsNativeControl(IElement e) => e.LocalName is "button" or "select" or "textarea" || (e.LocalName == "input" && e.GetAttribute("type") is not "hidden");

    private static bool IsLabelable(IElement e) => e.LocalName is "input" or "select" or "textarea" or "button" or "meter" or "output" or "progress";

    private static bool IsHidden(IElement e)
        => e.Ancestors<IElement>().Prepend(e).Any(static a => a.HasAttribute("hidden") || a.GetAttribute("aria-hidden") == "true"
            || (a.LocalName == "dialog" && !a.HasAttribute("open") && a.GetAttribute("data-state") == "closed")
            || (a.HasAttribute("popover") && a.GetAttribute("data-state") == "closed"));

    private static bool IsInteractive(IElement e)
        => e.LocalName is "button" or "select" or "textarea" or "details" || (e.LocalName == "a" && e.HasAttribute("href"))
           || (e.LocalName == "input" && e.GetAttribute("type") != "hidden")
           || (e.GetAttribute("tabindex") is { } t && int.TryParse(t, out var n) && n >= 0);

    private static bool IsFocusable(IElement e)
        => !e.HasAttribute("disabled") && (IsInteractive(e) || e.GetAttribute("tabindex") is { } t && int.TryParse(t, out var n) && n >= 0);

    /// <summary>Simplified accessible-name computation (aria-labelledby, aria-label, labels, alt/title, content).</summary>
    public static string AccessibleName(IElement element)
    {
        var document = element.Owner!;
        if (element.GetAttribute("aria-labelledby") is { Length: > 0 } labelledBy)
        {
            var text = string.Join(" ", labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(document.GetElementById)
                .Where(static e => e is not null).Select(static e => TextAlternative(e!)));
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        if (element.GetAttribute("aria-label") is { } label && !string.IsNullOrWhiteSpace(label)) return label.Trim();
        if (element.Id is { Length: > 0 } id && IsLabelable(element))
        {
            var labels = document.QuerySelectorAll("label").Where(l => l.GetAttribute("for") == id).Select(TextAlternative);
            var joined = string.Join(" ", labels).Trim();
            if (joined.Length > 0) return joined;
        }
        if (IsLabelable(element) && element.Closest("label") is { } wrapping) return TextAlternative(wrapping).Trim();
        if (element.LocalName == "input" && element.GetAttribute("type") is "button" or "submit" or "reset") return element.GetAttribute("value") ?? element.GetAttribute("type")!;
        var role = Role(element);
        if (role is "button" or "link" or "tab" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "option" or "treeitem" or "checkbox" or "radio" or "switch" or "heading" or "tooltip" || element.LocalName == "button")
        {
            var content = TextAlternative(element).Trim();
            if (content.Length > 0) return content;
        }
        if (element.LocalName == "dialog" || role is "dialog" or "alertdialog")
        {
            // A dialog without a label is commonly named by its first heading in some screen readers, but that is not reliable.
        }
        return element.GetAttribute("title")?.Trim() ?? (element.LocalName == "input" ? element.GetAttribute("placeholder") ?? "" : "");
    }

    private static string TextAlternative(IElement element)
    {
        var sb = new StringBuilder();
        void Walk(INode node)
        {
            if (node is IElement e)
            {
                if (e.GetAttribute("aria-hidden") == "true") return;
                if (e.LocalName == "img") { sb.Append(e.GetAttribute("alt")).Append(' '); return; }
                if (e.GetAttribute("aria-label") is { } l && e != element) { sb.Append(l).Append(' '); return; }
            }
            if (node is IText text) sb.Append(text.Data).Append(' ');
            foreach (var child in node.ChildNodes) Walk(child);
        }
        Walk(element);
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Describe(IElement element)
    {
        var html = element.OuterHtml;
        var end = html.IndexOf('>');
        var open = end > 0 ? html[..(end + 1)] : html;
        return open.Length > 200 ? open[..200] + "…" : open;
    }
}

/// <summary>Assertion helpers that fail with a readable list of issues. Framework-agnostic (throws <see cref="AccessibilityException"/>).</summary>
public static class AccessibilityAssert
{
    /// <summary>Fails when the markup has violations (warnings are reported only when <paramref name="failOnWarnings"/>).</summary>
    public static void NoViolations(string html, bool failOnWarnings = false, params string[] ignoreRules)
    {
        var issues = AccessibilityAudit.Audit(html)
            .Where(i => !ignoreRules.Contains(i.Rule) && (failOnWarnings || i.Severity == AuditSeverity.Violation)).ToList();
        if (issues.Count > 0) throw new AccessibilityException(issues);
    }
}

public sealed class AccessibilityException(IReadOnlyList<AccessibilityIssue> issues)
    : Exception($"{issues.Count} accessibility issue(s):\n" + string.Join("\n", issues))
{
    public IReadOnlyList<AccessibilityIssue> Issues { get; } = issues;
}
