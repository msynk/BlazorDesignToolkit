// Inspector for the demo: for any element of the page it shows the design tokens behind its styles and what assistive
// technology is told about it, and it logs live-region announcements. Everything is computed here from the live DOM and
// the page's style sheets, and sent as data to Inspector.razor, which renders it (page text is never written as HTML).
//
// The accessibility data is an approximation of the browser's accessibility tree (role, accessible name and description,
// states, position in set), close to what screen readers announce; real screen readers vary in wording.

let session = null;

const inherited = ["color", "font", "font-family", "font-size", "font-weight", "font-style", "line-height", "letter-spacing",
    "text-transform", "text-align", "white-space", "cursor", "accent-color", "list-style", "visibility"];
const blockedWhilePicking = ["pointerdown", "mousedown", "pointerup", "mouseup", "dblclick", "auxclick", "touchstart", "touchend"];
const maxStyles = 40;

export function start(dotnet, panel, prefix) {
    stop();
    const s = session = {
        dotnet, panel, prefix: `--${prefix}-`, picking: false, selected: null, last: "", frame: 0,
        regions: new Map(), cleanup: [],
    };
    s.highlight = overlay("inspector-highlight");
    s.selection = overlay("inspector-selection");

    const listen = (type, handler, options = { capture: true }) => {
        window.addEventListener(type, handler, options);
        s.cleanup.push(() => window.removeEventListener(type, handler, options));
    };
    listen("pointermove", e => {
        if (!s.picking) return;
        const target = pickable(e.target);
        place(s.highlight, target, target && describe(target));
    }, { capture: true, passive: true });
    listen("click", e => {
        if (!s.picking || isInspectorUi(e.target)) return;
        swallow(e);
        const target = pickable(e.target);
        if (target) select(target);
        setPicking(false, true);
    });
    for (const type of blockedWhilePicking) {
        listen(type, e => { if (s.picking && !isInspectorUi(e.target)) swallow(e); }, { capture: true, passive: false });
    }
    listen("keydown", e => {
        if (s.picking && e.key === "Escape") {
            swallow(e);
            setPicking(false, true);
        }
    });
    // Keyboard users inspect by moving focus: the inspector follows it.
    listen("focusin", e => { if (!isInspectorUi(e.target)) select(e.target); });
    // Scrolling and resizing only move the outlines; the element's data is unchanged.
    listen("scroll", () => schedule(false), { capture: true, passive: true });
    listen("resize", () => schedule(false), { passive: true });

    // One observer keeps the inspected element's data current (states change as it is used) and reports live regions.
    for (const region of liveRegions(document)) s.regions.set(region, text(region));
    const observer = new MutationObserver(mutations => {
        const changed = new Set();
        let relevant = false;
        for (const m of mutations) {
            const node = m.target.nodeType === Node.ELEMENT_NODE ? m.target : m.target.parentElement;
            if (!node || isInspectorUi(node)) continue;
            relevant = true;
            const region = node.closest(liveRegionSelector);
            if (region && region.getAttribute("aria-live") !== "off") changed.add(region);
            // Regions added with their content, like toasts with role="status" or "alert", are announced by most screen
            // reader and browser combinations as well.
            for (const added of m.addedNodes) {
                if (added.nodeType !== Node.ELEMENT_NODE) continue;
                for (const r of liveRegions(added)) changed.add(r);
            }
        }
        for (const region of changed) announce(region);
        if (relevant) schedule();
    });
    observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
    s.cleanup.push(() => observer.disconnect());

    if (document.activeElement && document.activeElement !== document.body && !isInspectorUi(document.activeElement)) {
        select(document.activeElement);
    }
}

export function stop() {
    const s = session;
    if (!s) return;
    session = null;
    cancelAnimationFrame(s.frame);
    for (const undo of s.cleanup) undo();
    s.highlight.remove();
    s.selection.remove();
    document.documentElement.classList.remove("inspector-picking");
}

export function setPicking(picking, notify = false) {
    const s = session;
    if (!s || s.picking === picking) return;
    s.picking = picking;
    document.documentElement.classList.toggle("inspector-picking", picking);
    if (!picking) place(s.highlight, null);
    if (notify) s.dotnet.invokeMethodAsync("OnPickingChanged", picking);
}

function swallow(e) {
    e.preventDefault();
    e.stopPropagation();
    e.stopImmediatePropagation();
}

function isInspectorUi(node) {
    const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    return !!element?.closest("[data-inspector-ui]");
}

// Clicks on text land on the nearest element; the root and body are too broad to be useful.
function pickable(target) {
    const element = target?.nodeType === Node.ELEMENT_NODE ? target : target?.parentElement;
    if (!element || element === document.documentElement || element === document.body || isInspectorUi(element)) return null;
    return element.closest("svg") ?? element;
}

function select(element) {
    const s = session;
    if (!s || !element || element === document.body || element === document.documentElement) return;
    s.selected = element;
    s.last = "";
    schedule();
}

function schedule(inspectAgain = true) {
    const s = session;
    if (!s) return;
    s.inspectAgain ||= inspectAgain;
    if (s.frame) return;
    s.frame = requestAnimationFrame(() => {
        s.frame = 0;
        const again = s.inspectAgain;
        s.inspectAgain = false;
        update(again);
    });
}

function update(inspectAgain) {
    const s = session;
    if (!s) return;
    if (s.picking && !s.highlight.hidden && s.highlightTarget) place(s.highlight, s.highlightTarget, describe(s.highlightTarget));
    if (!s.selected) return;
    const element = s.selected;
    place(s.selection, element.isConnected ? element : null);
    if (!inspectAgain) return;
    let data;
    try {
        data = inspect(element);
    } catch (error) {
        console.warn("Inspector:", error);
        return;
    }
    const json = JSON.stringify(data);
    if (json === s.last) return;
    s.last = json;
    s.dotnet.invokeMethodAsync("OnInspected", data);
}

// ---------------------------------------------------------------------------------------------------------------------
// Overlays

function overlay(className) {
    const element = document.createElement("div");
    element.className = className;
    element.setAttribute("aria-hidden", "true");
    element.setAttribute("data-inspector-ui", "");
    element.hidden = true;
    // In the top layer, so the outline is visible over modal dialogs and popovers too.
    if (typeof element.showPopover === "function") element.popover = "manual";
    const label = document.createElement("span");
    label.className = className + "__label";
    element.append(label);
    document.body.append(element);
    return element;
}

function place(box, target, label) {
    if (session && box === session.highlight) session.highlightTarget = target;
    const rect = target?.isConnected ? target.getBoundingClientRect() : null;
    if (!rect || (rect.width === 0 && rect.height === 0)) {
        box.hidden = true;
        if (box.popover && box.matches(":popover-open")) box.hidePopover();
        return;
    }
    box.hidden = false;
    if (box.popover) {
        // Showing again moves it above top-layer elements opened since.
        if (box.matches(":popover-open")) box.hidePopover();
        box.showPopover();
    }
    box.style.inset = `${rect.top}px auto auto ${rect.left}px`;
    box.style.width = `${rect.width}px`;
    box.style.height = `${rect.height}px`;
    box.firstChild.textContent = label ?? "";
    box.firstChild.hidden = !label;
}

// ---------------------------------------------------------------------------------------------------------------------
// Live regions

const liveRegionSelector = "[aria-live], [role=status], [role=alert], [role=log], [role=marquee], [role=timer], output";

function liveRegions(root) {
    const found = root.matches?.(liveRegionSelector) ? [root] : [];
    return [...found, ...root.querySelectorAll(liveRegionSelector)].filter(r => !isInspectorUi(r) && r.getAttribute("aria-live") !== "off");
}

function announce(region) {
    const s = session;
    const current = text(region);
    const previous = s.regions.get(region);
    s.regions.set(region, current);
    // Clearing a region announces nothing; it lets the next identical message be announced again.
    if (!current || current === previous) return;
    const role = region.getAttribute("role");
    const politeness = region.getAttribute("aria-live") ?? (role === "alert" ? "assertive" : "polite");
    const label = region.closest("[aria-label]")?.getAttribute("aria-label");
    s.dotnet.invokeMethodAsync("OnAnnounced", { text: current, politeness, source: label ? `${role ?? "live region"} in “${label}”` : role ?? "live region" });
}

function text(element) {
    return (element.textContent ?? "").replace(/\s+/g, " ").trim();
}

// ---------------------------------------------------------------------------------------------------------------------
// Inspection

function inspect(element) {
    const cache = new Map();
    return {
        element: describe(element),
        connected: element.isConnected,
        accessibility: accessibility(element),
        styles: element.isConnected ? styles(element, cache) : [],
    };
}

function describe(element) {
    let result = element.localName;
    if (element.id) result += "#" + element.id;
    const classes = [...element.classList].slice(0, 3);
    if (classes.length) result += "." + classes.join(".");
    const role = element.getAttribute("role");
    if (role) result += `[role=${role}]`;
    return result;
}

// ---------------------------------------------------------------------------------------------------------------------
// Accessibility

function accessibility(element) {
    const { role, source } = roleOf(element);
    const name = accessibleName(element, role);
    return {
        role: role ?? "",
        roleSource: source,
        name: name.name,
        nameSource: name.source,
        description: description(element, name.source),
        states: states(element, role),
        position: position(element, role) ?? "",
        focus: focusability(element),
        hidden: !!element.closest("[aria-hidden=true], [inert], [hidden]"),
        context: context(element),
        text: role && nameFromContent.has(role) ? "" : clip(text(element), 160),
    };
}

const nameFromContent = new Set(["button", "link", "tab", "menuitem", "menuitemcheckbox", "menuitemradio", "option", "treeitem",
    "checkbox", "radio", "switch", "heading", "cell", "gridcell", "columnheader", "rowheader", "tooltip", "row", "term"]);

function roleOf(element) {
    const explicit = element.getAttribute("role")?.trim().split(/\s+/)[0];
    if (explicit) return { role: explicit, source: "explicit" };
    const role = implicitRole(element);
    return { role, source: role ? "implicit" : "none" };
}

function implicitRole(element) {
    const tag = element.localName;
    const sectioning = "article, aside, main, nav, section";
    switch (tag) {
        case "a": case "area": return element.hasAttribute("href") ? "link" : null;
        case "button": case "summary": return "button";
        case "input": {
            const type = (element.getAttribute("type") ?? "text").toLowerCase();
            if (["button", "submit", "reset", "image"].includes(type)) return "button";
            if (type === "checkbox") return element.hasAttribute("switch") ? "switch" : "checkbox";
            if (type === "radio") return "radio";
            if (type === "range") return "slider";
            if (type === "number") return "spinbutton";
            if (type === "hidden" || type === "color" || type === "file") return null;
            if (element.hasAttribute("list")) return "combobox";
            return type === "search" ? "searchbox" : "textbox";
        }
        case "textarea": return "textbox";
        case "select": return element.multiple || element.size > 1 ? "listbox" : "combobox";
        case "option": return "option";
        case "optgroup": case "fieldset": case "details": return "group";
        case "h1": case "h2": case "h3": case "h4": case "h5": case "h6": return "heading";
        case "nav": return "navigation";
        case "main": return "main";
        case "aside": return "complementary";
        case "header": return element.parentElement?.closest(sectioning) ? null : "banner";
        case "footer": return element.parentElement?.closest(sectioning) ? null : "contentinfo";
        case "section": return element.hasAttribute("aria-label") || element.hasAttribute("aria-labelledby") ? "region" : null;
        case "form": return "form";
        case "search": return "search";
        case "article": return "article";
        case "ul": case "ol": case "menu": return "list";
        case "li": return "listitem";
        case "dt": return "term";
        case "dd": return "definition";
        case "table": return "table";
        case "caption": return "caption";
        case "thead": case "tbody": case "tfoot": return "rowgroup";
        case "tr": return "row";
        case "td": return "cell";
        case "th": return element.getAttribute("scope") === "row" ? "rowheader" : "columnheader";
        case "img": return element.getAttribute("alt") === "" ? "presentation" : "img";
        case "dialog": return "dialog";
        case "hr": return "separator";
        case "output": return "status";
        case "progress": return "progressbar";
        case "meter": return "meter";
        case "p": return "paragraph";
        case "blockquote": return "blockquote";
        case "code": return "code";
        case "strong": case "b": return "strong";
        case "em": case "i": return "emphasis";
        default: return null;
    }
}

function accessibleName(element, role) {
    const labelledBy = element.getAttribute("aria-labelledby")?.trim();
    if (labelledBy) {
        const ids = labelledBy.split(/\s+/);
        const name = clean(ids.map(id => document.getElementById(id)).filter(Boolean).map(n => content(n, n, null)).join(" "));
        if (name) return { name, source: "aria-labelledby → " + ids.map(id => "#" + id).join(" ") };
    }
    const label = element.getAttribute("aria-label")?.trim();
    if (label) return { name: label, source: "aria-label" };
    const tag = element.localName;
    const isButtonInput = tag === "input" && ["button", "submit", "reset"].includes(element.type);
    if (element.labels?.length && !isButtonInput) {
        const name = clean([...element.labels].map(l => content(l, l, element)).join(" "));
        if (name) return { name, source: "<label>" };
    }
    if ((tag === "img" || (tag === "input" && element.type === "image")) && element.getAttribute("alt")?.trim()) {
        return { name: element.getAttribute("alt").trim(), source: "alt" };
    }
    if (isButtonInput && element.value) return { name: element.value, source: "value" };
    const caption = tag === "fieldset" ? element.querySelector(":scope > legend") : tag === "table" ? element.querySelector(":scope > caption") : null;
    if (caption) {
        const name = clean(content(caption, caption, null));
        if (name) return { name, source: tag === "fieldset" ? "<legend>" : "<caption>" };
    }
    if (role && nameFromContent.has(role)) {
        const name = clean(content(element, element, null));
        if (name) return { name, source: "content" };
    }
    const title = element.getAttribute("title")?.trim();
    if (title) return { name: title, source: "title" };
    const placeholder = element.getAttribute("placeholder")?.trim();
    if (placeholder) return { name: placeholder, source: "placeholder (not a substitute for a label)" };
    return { name: "", source: "" };
}

// Text of a subtree as used for names: hidden content is skipped, nested labels and embedded controls contribute
// their own names or values.
function content(node, root, labelled) {
    if (node.nodeType === Node.TEXT_NODE) return node.textContent;
    if (node.nodeType !== Node.ELEMENT_NODE || node === labelled) return "";
    const element = node;
    if (element !== root && isHidden(element)) return "";
    if (element !== root) {
        const label = element.getAttribute("aria-label")?.trim();
        if (label) return " " + label + " ";
        const tag = element.localName;
        if (tag === "img") return " " + (element.getAttribute("alt") ?? "") + " ";
        if (tag === "input" && !["checkbox", "radio", "button", "submit", "reset", "hidden"].includes(element.type)) return " " + element.value + " ";
        if (tag === "select") return " " + [...element.selectedOptions].map(o => o.textContent).join(" ") + " ";
        if (tag === "svg" || tag === "script" || tag === "style") return "";
    }
    let result = "";
    for (const child of element.childNodes) result += content(child, root, labelled);
    const display = element === root ? "inline" : getComputedStyle(element).display;
    return display.startsWith("inline") || display === "contents" ? result : " " + result + " ";
}

function isHidden(element) {
    if (element.getAttribute("aria-hidden") === "true" || element.hidden) return true;
    const style = getComputedStyle(element);
    return style.display === "none" || style.visibility === "hidden";
}

function description(element, nameSource) {
    const describedBy = element.getAttribute("aria-describedby")?.trim();
    if (describedBy) {
        const value = clean(describedBy.split(/\s+/).map(id => document.getElementById(id)).filter(Boolean).map(n => content(n, n, null)).join(" "));
        if (value) return value;
    }
    const value = element.getAttribute("aria-description")?.trim();
    if (value) return value;
    const title = element.getAttribute("title")?.trim();
    return title && nameSource !== "title" ? title : "";
}

const stateAttributes = ["aria-pressed", "aria-selected", "aria-expanded", "aria-current", "aria-haspopup", "aria-invalid",
    "aria-required", "aria-readonly", "aria-busy", "aria-modal", "aria-multiselectable", "aria-orientation", "aria-sort",
    "aria-autocomplete", "aria-level", "aria-valuetext", "aria-valuenow", "aria-valuemin", "aria-valuemax", "aria-keyshortcuts"];

function states(element, role) {
    const out = [];
    const add = (name, value) => out.push({ name, value: String(value) });
    const tag = element.localName;
    if (tag === "input" && (element.type === "checkbox" || element.type === "radio")) {
        add("checked", element.indeterminate ? "mixed" : element.checked);
    } else if (element.hasAttribute("aria-checked")) {
        add("checked", element.getAttribute("aria-checked"));
    }
    for (const name of stateAttributes) {
        const value = element.getAttribute(name);
        if (value !== null && value !== "") add(name.slice(5), value);
    }
    if (/^h[1-6]$/.test(tag) && !element.hasAttribute("aria-level")) add("level", tag[1]);
    if (element.disabled || element.getAttribute("aria-disabled") === "true") add("disabled", true);
    if (element.required && !element.hasAttribute("aria-required")) add("required", true);
    if (tag === "input" && element.type === "range") {
        add("valuenow", element.value);
        add("valuemin", element.min || "0");
        add("valuemax", element.max || "100");
    } else if (["textbox", "searchbox", "spinbutton", "combobox"].includes(role) && (tag === "input" || tag === "textarea")) {
        add("value", element.value);
    } else if (tag === "select") {
        add("value", [...element.selectedOptions].map(o => o.textContent.trim()).join(", "));
    }
    if (tag === "dialog" && element.open && element.matches(":modal")) add("modal", true);
    for (const [attribute, label] of [["aria-controls", "controls"], ["aria-activedescendant", "active descendant"], ["aria-owns", "owns"]]) {
        const ids = element.getAttribute(attribute)?.trim();
        if (ids) add(label, ids.split(/\s+/).map(reference).join(", "));
    }
    return out;
}

function reference(id) {
    const target = document.getElementById(id);
    if (!target) return `#${id} (missing)`;
    const { role } = roleOf(target);
    const { name } = accessibleName(target, role);
    return name ? `${role ?? target.localName} “${clip(name, 40)}”` : `${role ?? target.localName} #${id}`;
}

const setContainers = {
    option: "[role=listbox], select, datalist",
    tab: "[role=tablist]",
    menuitem: "[role=menu], [role=menubar]",
    menuitemcheckbox: "[role=menu], [role=menubar]",
    menuitemradio: "[role=menu], [role=menubar]",
    treeitem: "[role=tree], [role=group]",
    radio: "[role=radiogroup]",
    listitem: "ul, ol, menu, [role=list]",
    row: "table, [role=grid], [role=table], [role=treegrid]",
};
const setMembers = {
    menuitem: ["menuitem", "menuitemcheckbox", "menuitemradio"],
    menuitemcheckbox: ["menuitem", "menuitemcheckbox", "menuitemradio"],
    menuitemradio: ["menuitem", "menuitemcheckbox", "menuitemradio"],
};

function position(element, role) {
    const posinset = element.getAttribute("aria-posinset");
    const setsize = element.getAttribute("aria-setsize");
    if (posinset && setsize) return `${posinset} of ${setsize}`;
    let items;
    if (role === "radio" && element.localName === "input" && element.name) {
        items = [...(element.form ?? document).querySelectorAll(`input[type=radio][name="${CSS.escape(element.name)}"]`)];
    } else {
        const selector = setContainers[role];
        const container = selector && element.parentElement?.closest(selector);
        if (!container) return null;
        const members = setMembers[role] ?? [role];
        items = [...container.querySelectorAll("*")].filter(n => members.includes(roleOf(n).role)
            && n.parentElement.closest(selector) === container && !n.closest("[hidden], [aria-hidden=true]"));
    }
    const index = items.indexOf(element);
    return index < 0 ? null : `${index + 1} of ${items.length}`;
}

function focusability(element) {
    if (element.disabled) return "Not focusable: disabled";
    if (element.closest("[inert]")) return "Not focusable: inside inert content";
    const tabindex = element.getAttribute("tabindex");
    if (tabindex !== null) {
        return Number(tabindex) >= 0
            ? `In the tab order (tabindex="${tabindex}")`
            : `Focusable by script only (tabindex="-1"): reached with arrow keys or moved to programmatically`;
    }
    return element.tabIndex >= 0 ? "In the tab order (native element)" : "Not focusable";
}

const contextRoles = new Set(["banner", "navigation", "main", "complementary", "contentinfo", "region", "form", "search",
    "dialog", "alertdialog", "tablist", "tabpanel", "menu", "menubar", "listbox", "tree", "radiogroup", "toolbar", "grid",
    "table", "group"]);

function context(element) {
    const result = [];
    for (let node = element.parentElement; node && node !== document.body; node = node.parentElement) {
        const { role } = roleOf(node);
        if (!role || !contextRoles.has(role)) continue;
        result.unshift({ role, name: clip(accessibleName(node, role).name, 40) });
    }
    return result.slice(-5);
}

function clean(value) {
    return value.replace(/\s+/g, " ").trim();
}

function clip(value, length) {
    return value.length > length ? value.slice(0, length - 1) + "…" : value;
}

// ---------------------------------------------------------------------------------------------------------------------
// Styles: which declarations apply to the element, and which custom properties (tokens) they use. A simplified cascade:
// !important, then unlayered before layered rules, then specificity, then order. Inherited properties are taken from the
// nearest ancestor that sets them.

function styles(element, cache) {
    const rules = cache.rules ??= collectRules();
    const own = winners(element, rules, cache);
    const result = [];
    const add = (property, declaration, from) => {
        if (result.length >= maxStyles || !declaration.value.includes("var(")) return;
        const variables = [...declaration.value.matchAll(/var\(\s*(--[\w-]+)/g)].map(m => chain(element, m[1], rules, cache));
        result.push({
            property,
            value: declaration.value,
            selector: declaration.selector,
            inheritedFrom: from ? describe(from) : "",
            variables,
        });
    };
    for (const [property, declaration] of own) {
        if (!property.startsWith("--")) add(property, declaration, null);
    }
    // Each inherited property comes from the nearest ancestor that sets it, or the font shorthand that covers it.
    const added = new Set();
    for (const property of inherited) {
        if (own.has(property) || (property.startsWith("font") && own.has("font"))) continue;
        for (let node = element.parentElement; node; node = node.parentElement) {
            const declarations = winners(node, rules, cache);
            const key = declarations.has(property) ? property : property.startsWith("font-") && declarations.has("font") ? "font" : null;
            if (!key) continue;
            const declaration = declarations.get(key);
            if (!added.has(declaration)) {
                added.add(declaration);
                add(key, declaration, node);
            }
            break;
        }
    }
    return result;
}

function chain(element, name, rules, cache) {
    const names = [name];
    while (!names.at(-1).startsWith(session.prefix) && names.length < 5) {
        const value = customValue(element, names.at(-1), rules, cache);
        const next = value?.match(/var\(\s*(--[\w-]+)/)?.[1];
        if (!next || names.includes(next)) break;
        names.push(next);
    }
    return names;
}

// Custom properties inherit: the nearest element that declares one decides its value.
function customValue(element, name, rules, cache) {
    for (let node = element; node; node = node.parentElement) {
        const declaration = winners(node, rules, cache).get(name);
        if (declaration) return declaration.value;
    }
    return null;
}

function winners(element, rules, cache) {
    if (cache.has(element)) return cache.get(element);
    const best = new Map();
    const consider = (property, candidate) => {
        const current = best.get(property);
        if (!current || compare(candidate, current) >= 0) best.set(property, candidate);
    };
    for (const entry of rules) {
        let matches = false;
        try {
            matches = element.matches(entry.selector);
        } catch {
            continue;
        }
        if (!matches) continue;
        const specificity = selectorSpecificity(element, entry.selector);
        for (const d of declarations(entry)) {
            consider(d.property, { value: d.value, important: d.important, layered: entry.layered, specificity, order: entry.order, selector: entry.selector });
        }
    }
    const inline = element.getAttribute("style");
    if (inline) {
        for (const d of parseDeclarations(inline)) {
            consider(d.property, { value: d.value, important: d.important, layered: false, specificity: [1, 0, 0, 0], order: Infinity, selector: "style attribute" });
        }
    }
    cache.set(element, best);
    return best;
}

function compare(a, b) {
    if (a.important !== b.important) return a.important ? 1 : -1;
    if (a.layered !== b.layered) return a.layered ? -1 : 1;
    for (let i = 0; i < 4; i++) {
        if (a.specificity[i] !== b.specificity[i]) return a.specificity[i] - b.specificity[i];
    }
    return a.order - b.order;
}

function collectRules() {
    const rules = [];
    let order = 0;
    const walk = (list, layered) => {
        for (const rule of list) {
            if (rule instanceof CSSStyleRule) {
                rules.push({ selector: rule.selectorText, style: rule.style, layered, order: order++, parsed: null });
            } else if (rule instanceof CSSMediaRule) {
                if (matchMedia(rule.media.mediaText).matches) walk(rule.cssRules, layered);
            } else if (rule instanceof CSSSupportsRule) {
                if (CSS.supports(rule.conditionText)) walk(rule.cssRules, layered);
            } else if (typeof CSSLayerBlockRule !== "undefined" && rule instanceof CSSLayerBlockRule) {
                walk(rule.cssRules, true);
            } else if (rule instanceof CSSImportRule && rule.styleSheet) {
                try { walk(rule.styleSheet.cssRules, layered || !!rule.layerName); } catch { /* cross-origin */ }
            }
        }
    };
    for (const sheet of document.styleSheets) {
        if (sheet.disabled) continue;
        if (sheet.media?.mediaText && !matchMedia(sheet.media.mediaText).matches) continue;
        try {
            walk(sheet.cssRules, false);
        } catch {
            // Style sheets from other origins cannot be read.
        }
    }
    return rules;
}

// Shorthands that use var() are only kept in their written form, so the serialized text is parsed instead of the
// (then empty) longhands.
function declarations(entry) {
    return entry.parsed ??= parseDeclarations(entry.style.cssText);
}

function parseDeclarations(text) {
    const result = [];
    for (const part of splitTopLevel(text, ";")) {
        const colon = part.indexOf(":");
        if (colon < 0) continue;
        const property = part.slice(0, colon).trim();
        let value = part.slice(colon + 1).trim();
        const important = /!\s*important$/i.test(value);
        if (important) value = value.replace(/!\s*important$/i, "").trim();
        if (property) result.push({ property: property.startsWith("--") ? property : property.toLowerCase(), value, important });
    }
    return result;
}

function splitTopLevel(text, separator) {
    const parts = [];
    let depth = 0;
    let quote = null;
    let start = 0;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quote) {
            if (c === "\\") i++;
            else if (c === quote) quote = null;
        } else if (c === "\"" || c === "'") {
            quote = c;
        } else if (c === "(" || c === "[") {
            depth++;
        } else if (c === ")" || c === "]") {
            depth--;
        } else if (c === separator && depth === 0) {
            parts.push(text.slice(start, i));
            start = i + 1;
        }
    }
    parts.push(text.slice(start));
    return parts.map(p => p.trim()).filter(Boolean);
}

// The highest specificity among the selectors of the list that match the element.
function selectorSpecificity(element, selectorList) {
    let best = [0, 0, 0, 0];
    for (const selector of splitTopLevel(selectorList, ",")) {
        let matches = false;
        try {
            matches = element.matches(selector);
        } catch {
            continue;
        }
        if (!matches) continue;
        const value = specificity(selector);
        if (compare({ important: false, layered: false, specificity: value, order: 0 }, { important: false, layered: false, specificity: best, order: 0 }) > 0) best = value;
    }
    return best;
}

function specificity(selector) {
    let a = 0, b = 0, c = 0;
    let rest = "";
    // Functional pseudo-classes: :where() counts nothing, :is()/:not()/:has() count their most specific argument.
    for (let i = 0; i < selector.length; i++) {
        const match = /^:(where|is|not|has|matches|-webkit-any)\(/i.exec(selector.slice(i));
        if (!match) {
            rest += selector[i];
            continue;
        }
        let depth = 1;
        let j = i + match[0].length;
        for (; j < selector.length && depth > 0; j++) {
            if (selector[j] === "(") depth++;
            else if (selector[j] === ")") depth--;
        }
        const inner = selector.slice(i + match[0].length, j - 1);
        if (match[1].toLowerCase() !== "where") {
            const max = splitTopLevel(inner, ",").map(specificity).reduce((m, s) => (s[1] * 1e6 + s[2] * 1e3 + s[3] > m[1] * 1e6 + m[2] * 1e3 + m[3] ? s : m), [0, 0, 0, 0]);
            a += max[1]; b += max[2]; c += max[3];
        }
        rest += " ";
        i = j - 1;
    }
    rest = rest.replace(/\[[^\]]*\]/g, () => { b++; return " "; })
        .replace(/:[\w-]+\([^)]*\)/g, () => { b++; return " "; })
        .replace(/#[\w-]+/g, () => { a++; return " "; })
        .replace(/\.[\w-]+/g, () => { b++; return " "; })
        .replace(/::[\w-]+/g, () => { c++; return " "; })
        .replace(/:[\w-]+/g, () => { b++; return " "; });
    c += (rest.match(/(^|[\s>+~])[a-zA-Z][\w-]*/g) ?? []).length;
    return [0, a, b, c];
}
