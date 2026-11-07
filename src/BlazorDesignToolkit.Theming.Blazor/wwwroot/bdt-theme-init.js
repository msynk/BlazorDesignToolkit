// BlazorDesignToolkit early theme initialization (classic script, run synchronously from <head>).
// Applies the persisted theme preference to the document element before the first paint to avoid a flash of the
// wrong theme. Configuration comes from the script element's data-config attribute; values are validated against
// the declared themes and axes, so storage tampering cannot inject arbitrary attribute values. Never throws.
(function () {
  try {
    var script = document.currentScript;
    if (!script) return;
    var config = JSON.parse(script.getAttribute('data-config') || '{}');
    var root = document.documentElement;
    var stored = null;
    if (config.storage === 'localStorage') {
      try { stored = window.localStorage.getItem(config.key); } catch (e) { stored = null; }
    } else if (config.storage === 'cookie') {
      var parts = (document.cookie || '').split(';');
      for (var i = 0; i < parts.length; i++) {
        var p = parts[i].trim();
        if (p.indexOf(config.key + '=') === 0) {
          try { stored = decodeURIComponent(p.substring(config.key.length + 1)); } catch (e) { stored = null; }
        }
      }
    }
    if (!stored) return;
    var values = {};
    var entries = stored.split(';');
    for (var j = 0; j < entries.length; j++) {
      var kv = entries[j].split('=');
      if (kv.length === 2) values[kv[0].trim()] = kv[1].trim();
    }
    if (values.theme && config.themes && config.themes.indexOf(values.theme) >= 0) {
      root.setAttribute(config.themeAttribute, values.theme);
    }
    var axes = config.axes || {};
    for (var axis in axes) {
      if (!Object.prototype.hasOwnProperty.call(axes, axis)) continue;
      var value = values[axis];
      if (value && axes[axis].indexOf(value) >= 0) root.setAttribute(config.axisPrefix + axis, value);
    }
  } catch (e) {
    // Fall back to the server-rendered or default theme.
  }
})();
