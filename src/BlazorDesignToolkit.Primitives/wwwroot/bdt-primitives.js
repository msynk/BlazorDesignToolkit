// BlazorDesignToolkit headless primitives: the small set of behaviors that genuinely need browser APIs.
// ES module loaded on demand through JS interop. No globals, no styling. Every function tolerates missing elements.

const handles = new Map();
let nextHandle = 1;

function register(dispose) {
  const id = nextHandle++;
  handles.set(id, dispose);
  return id;
}

/** Disposes any handle returned by this module. */
export function release(id) {
  const dispose = handles.get(id);
  if (dispose) {
    handles.delete(id);
    try { dispose(); } catch { /* element already gone */ }
  }
}

// ---- Keyboard ---------------------------------------------------------------------------------------------

function keyId(event) {
  const parts = [];
  if (event.ctrlKey) parts.push('Ctrl');
  if (event.metaKey) parts.push('Meta');
  if (event.altKey) parts.push('Alt');
  parts.push(event.key === ' ' ? 'Space' : event.key);
  return parts.join('+');
}

/**
 * Prevents the browser default (scrolling, form submission, caret movement) for the keys listed in the element's
 * data-bdt-keys attribute at the time of the event. Components update the attribute on render, so the list follows
 * component state without further interop calls.
 */
export function preventKeys(element) {
  if (!element) return 0;
  const handler = (event) => {
    const list = element.getAttribute('data-bdt-keys');
    if (!list) return;
    const keys = list.split(' ');
    if (keys.includes(keyId(event)) || (keys.includes(event.key === ' ' ? 'Space' : event.key) && !event.ctrlKey && !event.metaKey && !event.altKey)) {
      event.preventDefault();
    }
  };
  element.addEventListener('keydown', handler);
  return register(() => element.removeEventListener('keydown', handler));
}

// ---- Focus ------------------------------------------------------------------------------------------------

export function focusElement(element, preventScroll) {
  if (element && typeof element.focus === 'function') element.focus({ preventScroll: !!preventScroll });
}

export function focusById(id, preventScroll) {
  focusElement(document.getElementById(id), preventScroll);
}

export function scrollIntoViewById(id) {
  const el = id ? document.getElementById(id) : null;
  if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

export function setIndeterminate(element, value) {
  if (element) element.indeterminate = !!value;
}

/** Returns the ids of elements matching selector inside container, in document order. */
export function domOrder(container, selector) {
  if (!container) return [];
  return Array.from(container.querySelectorAll(selector)).map(e => e.id).filter(Boolean);
}

/** Id of the focused element (used when a focus event has not reached .NET yet). */
export function activeElementId() {
  return document.activeElement?.id ?? null;
}

export function isFocusWithin(element) {
  return !!element && element.contains(document.activeElement);
}

const focusableSelector = [
  'a[href]', 'area[href]', 'button:not([disabled])', 'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
  'textarea:not([disabled])', 'iframe', 'object', 'embed', '[contenteditable="true"]', '[tabindex]:not([tabindex="-1"])',
].join(',');

function focusables(container) {
  return Array.from(container.querySelectorAll(focusableSelector))
    .filter(el => !el.hasAttribute('inert') && !el.closest('[inert]') && el.getClientRects().length > 0);
}

/** Keeps Tab and Shift+Tab inside container (for non-modal overlays; modal dialogs use native inertness). */
export function trapFocus(container, restoreFocus) {
  if (!container) return 0;
  const previous = document.activeElement;
  const onKeyDown = (event) => {
    if (event.key !== 'Tab') return;
    const items = focusables(container);
    if (items.length === 0) { event.preventDefault(); container.focus?.(); return; }
    const first = items[0];
    const last = items[items.length - 1];
    if (event.shiftKey && (document.activeElement === first || !container.contains(document.activeElement))) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && (document.activeElement === last || !container.contains(document.activeElement))) {
      event.preventDefault(); first.focus();
    }
  };
  const onFocusIn = (event) => {
    if (!container.contains(event.target)) {
      const items = focusables(container);
      (items[0] ?? container).focus?.();
    }
  };
  document.addEventListener('keydown', onKeyDown, true);
  document.addEventListener('focusin', onFocusIn, true);
  if (!container.contains(document.activeElement)) (focusables(container)[0] ?? container).focus?.();
  return register(() => {
    document.removeEventListener('keydown', onKeyDown, true);
    document.removeEventListener('focusin', onFocusIn, true);
    if (restoreFocus && previous && typeof previous.focus === 'function' && document.contains(previous)) previous.focus();
  });
}

// ---- Dialog -----------------------------------------------------------------------------------------------

/**
 * Opens a native <dialog>. Modal dialogs use showModal(), which makes the rest of the page inert and moves the dialog
 * to the top layer without moving it in the DOM (so Blazor's renderer is unaffected). Focus returns to the previously
 * focused element when the dialog closes.
 */
export function showDialog(dialog, modal, dotNetRef, options) {
  if (!dialog) return 0;
  const previous = document.activeElement;
  const root = document.documentElement;
  const previousOverflow = root.style.overflow;
  const onCancel = (event) => {
    event.preventDefault();
    if (options?.closeOnEscape !== false) dotNetRef.invokeMethodAsync('OnDialogDismiss', 'escape').catch(() => {});
  };
  const onClick = (event) => {
    if (event.target !== dialog || options?.closeOnBackdropClick !== true) return;
    const r = dialog.getBoundingClientRect();
    const inside = event.clientX >= r.left && event.clientX <= r.right && event.clientY >= r.top && event.clientY <= r.bottom;
    if (!inside) dotNetRef.invokeMethodAsync('OnDialogDismiss', 'backdrop').catch(() => {});
  };
  dialog.addEventListener('cancel', onCancel);
  dialog.addEventListener('click', onClick);
  try {
    if (!dialog.open) modal ? dialog.showModal() : dialog.show();
  } catch {
    dialog.setAttribute('open', '');
  }
  if (modal && options?.lockScroll !== false) root.style.overflow = 'hidden';
  const initial = options?.initialFocus ? dialog.querySelector(options.initialFocus) : null;
  if (initial) initial.focus();
  return register(() => {
    dialog.removeEventListener('cancel', onCancel);
    dialog.removeEventListener('click', onClick);
    if (dialog.open) dialog.close();
    root.style.overflow = previousOverflow;
    if (options?.restoreFocus !== false && previous && typeof previous.focus === 'function' && document.contains(previous)) previous.focus();
  });
}

// ---- Popover and positioning ------------------------------------------------------------------------------

const supportsPopover = typeof HTMLElement !== 'undefined' && Object.prototype.hasOwnProperty.call(HTMLElement.prototype, 'popover');

function place(floating, anchor, placement, offset) {
  if (!floating || !anchor) return;
  const a = anchor.getBoundingClientRect();
  const f = floating.getBoundingClientRect();
  const vw = document.documentElement.clientWidth;
  const vh = document.documentElement.clientHeight;
  const margin = 8;
  let [side, align] = (placement || 'bottom-start').split('-');
  const fits = {
    bottom: a.bottom + offset + f.height <= vh - margin,
    top: a.top - offset - f.height >= margin,
    right: a.right + offset + f.width <= vw - margin,
    left: a.left - offset - f.width >= margin,
  };
  const opposite = { bottom: 'top', top: 'bottom', left: 'right', right: 'left' };
  if (!fits[side] && fits[opposite[side]]) side = opposite[side];
  let top;
  let left;
  if (side === 'bottom' || side === 'top') {
    top = side === 'bottom' ? a.bottom + offset : a.top - offset - f.height;
    left = align === 'end' ? a.right - f.width : align === 'center' ? a.left + (a.width - f.width) / 2 : a.left;
  } else {
    left = side === 'right' ? a.right + offset : a.left - offset - f.width;
    top = align === 'end' ? a.bottom - f.height : align === 'center' ? a.top + (a.height - f.height) / 2 : a.top;
  }
  left = Math.max(margin, Math.min(left, vw - f.width - margin));
  top = Math.max(margin, Math.min(top, vh - f.height - margin));
  floating.style.position = 'fixed';
  floating.style.inset = 'auto';
  floating.style.margin = '0';
  floating.style.top = `${Math.round(top)}px`;
  floating.style.left = `${Math.round(left)}px`;
  floating.setAttribute('data-side', side);
}

/**
 * Shows a popover in the top layer (Popover API) and keeps it positioned next to its anchor. Light dismiss
 * (click outside, Escape) is native for popover="auto"; .NET is told through OnPopoverToggle so its state stays in sync.
 */
export function showPopover(floating, anchor, dotNetRef, options) {
  if (!floating) return 0;
  const placement = options?.placement ?? 'bottom-start';
  const offset = options?.offset ?? 4;
  const reposition = () => requestAnimationFrame(() => place(floating, anchor, placement, offset));
  // Browsers differ in where focus goes when a popover containing it is hidden (WebKit moves it to the closest focusable
  // ancestor), so remember before hiding whether focus was inside.
  let focusWasInside = false;
  const onBeforeToggle = (event) => {
    if (event.newState === 'closed') focusWasInside = floating.contains(document.activeElement);
  };
  const onToggle = (event) => {
    if (event.newState === 'closed') dotNetRef?.invokeMethodAsync('OnPopoverToggle', false).catch(() => {});
  };
  let fallbackCleanup = null;
  if (supportsPopover && floating.hasAttribute('popover')) {
    floating.addEventListener('beforetoggle', onBeforeToggle);
    floating.addEventListener('toggle', onToggle);
    try {
      if (!floating.matches(':popover-open')) floating.showPopover(anchor ? { source: anchor } : undefined);
    } catch {
      try { floating.showPopover(); } catch { /* already open or detached */ }
    }
  } else {
    // Fallback for browsers without the Popover API: show in place and emulate light dismiss.
    floating.style.display = 'block';
    floating.style.zIndex = '2147483000';
    const onPointer = (event) => {
      if (!floating.contains(event.target) && !(anchor && anchor.contains(event.target))) {
        dotNetRef?.invokeMethodAsync('OnPopoverToggle', false).catch(() => {});
      }
    };
    const onKey = (event) => { if (event.key === 'Escape') dotNetRef?.invokeMethodAsync('OnPopoverToggle', false).catch(() => {}); };
    document.addEventListener('pointerdown', onPointer, true);
    document.addEventListener('keydown', onKey, true);
    fallbackCleanup = () => {
      document.removeEventListener('pointerdown', onPointer, true);
      document.removeEventListener('keydown', onKey, true);
      floating.style.display = '';
    };
  }
  place(floating, anchor, placement, offset);
  window.addEventListener('scroll', reposition, true);
  window.addEventListener('resize', reposition);
  const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(reposition) : null;
  observer?.observe(floating);
  if (options?.focus) {
    const target = floating.querySelector(options.focus === true ? focusableSelector : options.focus);
    (target ?? floating).focus?.({ preventScroll: true });
  }
  return register(() => {
    const active = document.activeElement;
    const hadFocus = focusWasInside || !active || active === document.body || active === document.documentElement || floating.contains(active);
    floating.removeEventListener('beforetoggle', onBeforeToggle);
    floating.removeEventListener('toggle', onToggle);
    window.removeEventListener('scroll', reposition, true);
    window.removeEventListener('resize', reposition);
    observer?.disconnect();
    fallbackCleanup?.();
    if (supportsPopover && floating.hasAttribute('popover')) {
      try { if (floating.matches(':popover-open')) floating.hidePopover(); } catch { /* already hidden */ }
    }
    if (options?.restoreFocus !== false && hadFocus && anchor && document.contains(anchor)) anchor.focus({ preventScroll: true });
  });
}

// ---- Tooltip ----------------------------------------------------------------------------------------------

/**
 * Tooltip behavior implemented entirely in the browser so hover and focus never wait for a server round trip.
 * Meets WCAG 2.2 SC 1.4.13: dismissible with Escape without moving focus, hoverable (pointer may move onto the
 * tooltip), and persistent until hover/focus leaves or it is dismissed.
 */
export function attachTooltip(trigger, tooltip, options) {
  if (!trigger || !tooltip) return 0;
  const showDelay = options?.showDelay ?? 300;
  const hideDelay = options?.hideDelay ?? 150;
  const placement = options?.placement ?? 'top-center';
  let showTimer = 0;
  let hideTimer = 0;
  let open = false;
  const show = () => {
    clearTimeout(hideTimer);
    if (open) return;
    open = true;
    if (supportsPopover && tooltip.hasAttribute('popover')) { try { tooltip.showPopover(); } catch { } } else tooltip.hidden = false;
    tooltip.setAttribute('data-state', 'open');
    place(tooltip, trigger, placement, 6);
  };
  const hide = () => {
    clearTimeout(showTimer);
    if (!open) return;
    open = false;
    if (supportsPopover && tooltip.hasAttribute('popover')) { try { tooltip.hidePopover(); } catch { } } else tooltip.hidden = true;
    tooltip.setAttribute('data-state', 'closed');
  };
  const scheduleShow = (delay) => { clearTimeout(hideTimer); clearTimeout(showTimer); showTimer = setTimeout(show, delay); };
  const scheduleHide = () => { clearTimeout(showTimer); clearTimeout(hideTimer); hideTimer = setTimeout(hide, hideDelay); };
  const onKey = (event) => { if (event.key === 'Escape' && open) { hide(); } };
  const listeners = [
    [trigger, 'pointerenter', () => scheduleShow(showDelay)],
    [trigger, 'pointerleave', scheduleHide],
    [trigger, 'focusin', () => scheduleShow(0)],
    [trigger, 'focusout', scheduleHide],
    [tooltip, 'pointerenter', () => clearTimeout(hideTimer)],
    [tooltip, 'pointerleave', scheduleHide],
    [document, 'keydown', onKey],
  ];
  for (const [target, name, fn] of listeners) target.addEventListener(name, fn);
  if (!supportsPopover || !tooltip.hasAttribute('popover')) tooltip.hidden = true;
  tooltip.setAttribute('data-tooltip-ready', '');
  // The trigger may already have keyboard focus when the behavior attaches (for example right after hydration).
  try { if (trigger.matches(':focus-visible')) scheduleShow(0); } catch { /* :focus-visible unsupported */ }
  return register(() => {
    clearTimeout(showTimer);
    clearTimeout(hideTimer);
    for (const [target, name, fn] of listeners) target.removeEventListener(name, fn);
    hide();
  });
}

// ---- Global hotkeys ---------------------------------------------------------------------------------------

/** Registers a page-wide shortcut such as "Ctrl+K" (Meta is accepted for Ctrl on macOS). */
export function registerHotkey(combo, dotNetRef, method) {
  const wanted = combo.split('+').map(p => p.trim().toLowerCase());
  const key = wanted[wanted.length - 1];
  const needsCtrl = wanted.includes('ctrl') || wanted.includes('mod');
  const needsAlt = wanted.includes('alt');
  const needsShift = wanted.includes('shift');
  const handler = (event) => {
    const ctrl = event.ctrlKey || event.metaKey;
    if (event.key.toLowerCase() === key && ctrl === needsCtrl && event.altKey === needsAlt && event.shiftKey === needsShift) {
      event.preventDefault();
      dotNetRef.invokeMethodAsync(method).catch(() => {});
    }
  };
  document.addEventListener('keydown', handler);
  return register(() => document.removeEventListener('keydown', handler));
}
