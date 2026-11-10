// Live markup inspector for the demo: renders the DOM of an example as indented markup and keeps it up to date while the
// example is used, so the ARIA roles, states and relationships maintained by the primitives are visible. Accessibility
// attributes are highlighted. Text from the page is only ever written with textContent.

const observers = new WeakMap();
const maxLines = 400;
const maxText = 60;

// Attributes rendered by Blazor itself, by CSS isolation, internal hooks of the primitives' script (data-bdt-*) and inline
// styles (theme overrides) are noise here.
const isInternal = (name) => name.startsWith("_bl_") || name.startsWith("data-bdt-") || /^b-[a-z0-9]{6,}$/.test(name) || name === "style";
const isAccessibility = (name) =>
    name === "role" || name.startsWith("aria-") || name === "tabindex" || name === "hidden" || name === "inert" ||
    name === "disabled" || name === "open" || name === "popover" || name === "for" || name === "id" || name === "data-state";

export function observe(output, targetId) {
    stop(output);
    const target = document.getElementById(targetId);
    if (!target) return;
    let scheduled = false;
    const render = () => {
        scheduled = false;
        draw(output, target);
    };
    const observer = new MutationObserver(() => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(render);
    });
    observer.observe(target, { subtree: true, childList: true, attributes: true, characterData: true });
    observers.set(output, observer);
    render();
}

export function stop(output) {
    observers.get(output)?.disconnect();
    observers.delete(output);
    output.replaceChildren();
}

function draw(output, target) {
    const fragment = document.createDocumentFragment();
    const state = { lines: 0, truncated: 0 };
    for (const child of target.childNodes) write(fragment, child, 0, state);
    if (state.truncated > 0) {
        fragment.append(span("comment", `<!-- ${state.truncated} more element(s) not shown -->\n`));
    }
    output.replaceChildren(fragment);
}

function write(out, node, depth, state) {
    if (node.nodeType === Node.TEXT_NODE) {
        const text = node.textContent.replace(/\s+/g, " ").trim();
        if (!text) return;
        if (state.lines >= maxLines) return;
        state.lines++;
        out.append(indent(depth), span("text", text.length > maxText ? text.slice(0, maxText - 1) + "…" : text), "\n");
        return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;
    const tag = node.localName;
    if (tag === "script" || tag === "style") return;
    if (state.lines >= maxLines) {
        state.truncated++;
        return;
    }
    state.lines++;
    out.append(indent(depth), span("tag", "<" + tag));
    for (const attribute of node.attributes) {
        if (isInternal(attribute.name)) continue;
        const kind = isAccessibility(attribute.name) ? "attr attr--a11y" : "attr";
        out.append(" ", span(kind, attribute.value === "" ? attribute.name : `${attribute.name}="${attribute.value}"`));
    }
    // svg drawings are summarized: their paths say nothing about behavior.
    const children = tag === "svg" ? [] : [...node.childNodes].filter(c => c.nodeType === Node.ELEMENT_NODE || c.textContent.trim());
    if (children.length === 0) {
        out.append(span("tag", tag === "svg" ? ">…</svg>" : ` />`), "\n");
        return;
    }
    // A single short text child stays on the element's line.
    if (children.length === 1 && children[0].nodeType === Node.TEXT_NODE) {
        const text = children[0].textContent.replace(/\s+/g, " ").trim();
        out.append(span("tag", ">"), span("text", text.length > maxText ? text.slice(0, maxText - 1) + "…" : text), span("tag", `</${tag}>`), "\n");
        return;
    }
    out.append(span("tag", ">"), "\n");
    for (const child of children) write(out, child, depth + 1, state);
    if (state.lines < maxLines) {
        state.lines++;
        out.append(indent(depth), span("tag", `</${tag}>`), "\n");
    }
}

function indent(depth) {
    return "  ".repeat(depth);
}

function span(kind, text) {
    const element = document.createElement("span");
    element.className = "markup-" + kind.replaceAll(" ", " markup-");
    element.textContent = text;
    return element;
}
