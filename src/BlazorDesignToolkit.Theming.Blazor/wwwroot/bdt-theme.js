// BlazorDesignToolkit theming module. Loaded on demand through JS interop; exports only, no globals.
// Every operation is defensive: missing elements, blocked storage and disconnected circuits never throw into the page.

const attributePattern = /^[a-zA-Z][a-zA-Z0-9_-]*$/;
const applied = new Map(); // rootSelector -> { attributes, properties }
let enhancedLoadHooked = false;

function resolveRoot(rootSelector) {
  if (!rootSelector || rootSelector === ':root' || rootSelector === 'html') return document.documentElement;
  return document.querySelector(rootSelector);
}

function write(root, attributes, properties, clearProperties) {
  for (const [name, value] of Object.entries(attributes ?? {})) {
    if (!attributePattern.test(name)) continue;
    if (value === null || value === undefined) root.removeAttribute(name);
    else if (root.getAttribute(name) !== value) root.setAttribute(name, String(value));
  }
  for (const name of clearProperties ?? []) {
    if (name.startsWith('--')) root.style.removeProperty(name);
  }
  for (const [name, value] of Object.entries(properties ?? {})) {
    if (!name.startsWith('--')) continue;
    if (value === null || value === undefined) root.style.removeProperty(name);
    else root.style.setProperty(name, String(value));
  }
}

function hookEnhancedLoad() {
  if (enhancedLoadHooked) return;
  enhancedLoadHooked = true;
  // Enhanced navigation can patch attributes of the document element; re-apply the last known state afterwards.
  const blazor = globalThis.Blazor;
  if (blazor && typeof blazor.addEventListener === 'function') {
    blazor.addEventListener('enhancedload', () => {
      for (const [selector, state] of applied) {
        const root = resolveRoot(selector);
        if (root) write(root, state.attributes, state.properties, []);
      }
    });
  }
}

/** Applies theme attributes and inline custom properties in one batch. Returns false when the root element is missing. */
export function apply(rootSelector, attributes, properties, clearProperties) {
  const root = resolveRoot(rootSelector);
  if (!root) return false;
  write(root, attributes, properties, clearProperties);
  const previous = applied.get(rootSelector) ?? { attributes: {}, properties: {} };
  const props = { ...previous.properties };
  for (const name of clearProperties ?? []) delete props[name];
  Object.assign(props, properties ?? {});
  applied.set(rootSelector, { attributes: { ...previous.attributes, ...attributes }, properties: props });
  hookEnhancedLoad();
  return true;
}

/** Reads the current attribute values (used to adopt state applied by the early-initialization script). */
export function readAttributes(rootSelector, names) {
  const root = resolveRoot(rootSelector);
  const result = {};
  if (!root) return result;
  for (const name of names ?? []) {
    if (attributePattern.test(name)) result[name] = root.getAttribute(name);
  }
  return result;
}

const queries = {
  dark: '(prefers-color-scheme: dark)',
  more: '(prefers-contrast: more)',
  less: '(prefers-contrast: less)',
  custom: '(prefers-contrast: custom)',
  forced: '(forced-colors: active)',
  reduce: '(prefers-reduced-motion: reduce)',
};

function matches(query) {
  try { return globalThis.matchMedia ? globalThis.matchMedia(query).matches : false; } catch { return false; }
}

/** Current operating-system preferences. */
export function getSystemPreferences() {
  if (!globalThis.matchMedia) return { colorScheme: null, contrast: null, forcedColors: false, reducedMotion: false };
  return {
    colorScheme: matches(queries.dark) ? 'dark' : 'light',
    contrast: matches(queries.more) ? 'more' : matches(queries.less) ? 'less' : matches(queries.custom) ? 'custom' : 'no-preference',
    forcedColors: matches(queries.forced),
    reducedMotion: matches(queries.reduce),
  };
}

const watchers = new Map();
let nextWatcherId = 1;

/** Notifies .NET (OnSystemPreferencesChanged) whenever a relevant media feature changes. Returns a handle for unwatch. */
export function watchSystemPreferences(dotNetRef) {
  if (!globalThis.matchMedia) return 0;
  const lists = Object.values(queries).map(q => globalThis.matchMedia(q));
  const handler = () => {
    dotNetRef.invokeMethodAsync('OnSystemPreferencesChanged', getSystemPreferences()).catch(() => { /* circuit gone */ });
  };
  for (const list of lists) list.addEventListener('change', handler);
  const id = nextWatcherId++;
  watchers.set(id, () => { for (const list of lists) list.removeEventListener('change', handler); });
  return id;
}

export function unwatch(id) {
  const dispose = watchers.get(id);
  if (dispose) { dispose(); watchers.delete(id); }
}

/** localStorage access that tolerates privacy modes and disabled storage. */
export function loadStoredPreference(key) {
  try { return globalThis.localStorage?.getItem(key) ?? null; } catch { return null; }
}

export function storePreference(key, value) {
  try { globalThis.localStorage?.setItem(key, value); return true; } catch { return false; }
}

export function clearStoredPreference(key) {
  try { globalThis.localStorage?.removeItem(key); return true; } catch { return false; }
}

/** Writes a first-party preference cookie so server rendering can apply the theme without a flash. */
export function writePreferenceCookie(name, value, maxAgeSeconds) {
  if (!attributePattern.test(name)) return false;
  const secure = globalThis.location?.protocol === 'https:' ? '; Secure' : '';
  const encoded = value === null ? '' : encodeURIComponent(value);
  const age = value === null ? 0 : maxAgeSeconds;
  document.cookie = name + '=' + encoded + '; Path=/; Max-Age=' + age + '; SameSite=Lax' + secure;
  return true;
}

export function readPreferenceCookie(name) {
  const prefix = name + '=';
  for (const part of (document.cookie || '').split(';')) {
    const trimmed = part.trim();
    if (trimmed.startsWith(prefix)) {
      try { return decodeURIComponent(trimmed.substring(prefix.length)); } catch { return null; }
    }
  }
  return null;
}
