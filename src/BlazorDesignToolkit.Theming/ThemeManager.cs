using System.Collections.Immutable;
using System.Text;
using BlazorDesignToolkit.Tokens;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Theming;

/// <summary>A persisted theme preference: theme id and requested axis values (which may be <c>system</c>).</summary>
public sealed record ThemePreference(string ThemeId, ImmutableSortedDictionary<string, string> Axes)
{
    /// <summary>Compact, cookie- and storage-safe text form: <c>theme=aurora;mode=dark;density=compact</c>.</summary>
    public string Format()
    {
        var sb = new StringBuilder("theme=").Append(ThemeId);
        foreach (var (k, v) in Axes) sb.Append(';').Append(k).Append('=').Append(v);
        return sb.ToString();
    }

    /// <summary>Parses <see cref="Format"/> output. Rejects anything that is not made of valid identifiers.</summary>
    public static bool TryParse(string? text, out ThemePreference preference)
    {
        preference = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024) return false;
        string? theme = null;
        var axes = ImmutableSortedDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) return false;
            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (!ThemeIdentifiers.IsValid(key) || !ThemeIdentifiers.IsValid(value)) return false;
            if (key == "theme") theme = value;
            else axes = axes.SetItem(key, value);
        }
        if (theme is null) return false;
        preference = new ThemePreference(theme, axes);
        return true;
    }
}

/// <summary>Optional persistence of the user's theme choice. Implementations must tolerate missing or blocked storage.</summary>
public interface IThemePreferenceStore
{
    ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Stores nothing. The default: no persistence, no privacy implications.</summary>
public sealed class NullThemePreferenceStore : IThemePreferenceStore
{
    public static NullThemePreferenceStore Instance { get; } = new();
    public ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<ThemePreference?>(null);
    public ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Keeps the preference in memory (tests, server-side sessions).</summary>
public sealed class InMemoryThemePreferenceStore : IThemePreferenceStore
{
    public ThemePreference? Value { get; private set; }
    public ValueTask<ThemePreference?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Value);
    public ValueTask SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default) { Value = preference; return ValueTask.CompletedTask; }
    public ValueTask ClearAsync(CancellationToken cancellationToken = default) { Value = null; return ValueTask.CompletedTask; }
}

[Flags]
public enum ThemeChangeKind
{
    None = 0,
    Theme = 1,
    Axes = 2,
    Overrides = 4,
    SystemPreferences = 8,
    Reset = 16,
}

/// <summary>Immutable snapshot of the theme manager state.</summary>
public sealed record ThemeState
{
    /// <summary>Requested selection; axis values may be <c>system</c>.</summary>
    public required ThemeSelection Selection { get; init; }
    /// <summary>Selection with <c>system</c> replaced using the latest system preferences.</summary>
    public required ThemeSelection EffectiveSelection { get; init; }
    public required ImmutableSortedDictionary<string, SourceNode> Overrides { get; init; }
    public required SystemPreferences SystemPreferences { get; init; }

    public string ThemeId => Selection.ThemeId;

    public SourceObject? OverridesTree => Overrides.IsEmpty ? null : ThemeOverrides.ToTree(Overrides);
}

/// <summary>Helpers for runtime token overrides (token path → DTCG <c>$value</c>).</summary>
public static class ThemeOverrides
{
    /// <summary>Builds the partial token tree that <see cref="ThemeCatalog.Resolve(ThemeSelection, SourceObject?, CancellationToken)"/> layers on top of a theme.</summary>
    public static SourceObject ToTree(IEnumerable<KeyValuePair<string, SourceNode>> overrides)
    {
        var tree = SourceObject.Empty;
        foreach (var (path, value) in overrides.OrderBy(static o => o.Key, StringComparer.Ordinal))
        {
            var segments = TokenPath.Parse(path).Segments;
            SourceObject node = SourceObject.Create(("$value", value));
            for (var i = segments.Length - 1; i >= 0; i--) node = SourceObject.Create((segments[i], node));
            tree = TokenTreeMerger.Merge(tree, node);
        }
        return tree;
    }
}

public sealed class ThemeChangedEventArgs(ThemeState previous, ThemeState current, ThemeChangeKind kind) : EventArgs
{
    public ThemeState Previous { get; } = previous;
    public ThemeState Current { get; } = current;
    public ThemeChangeKind Kind { get; } = kind;
}

/// <summary>
/// Browser-independent runtime theme state: selected theme, axis values (including <c>system</c>), runtime token overrides,
/// change notifications with batching, and optional persistence. One instance per user scope (circuit or WebAssembly app).
/// Invalid input never throws from <c>Try*</c> methods and never leaves the manager in an invalid state.
/// </summary>
public sealed class ThemeManager
{
    private readonly IThemePreferenceStore _store;
    private ThemeState _state;
    private int _batchDepth;
    private ThemeState? _batchStart;
    private ThemeChangeKind _batchKind;

    public ThemeManager(ThemeCatalog catalog, IThemePreferenceStore? store = null, ThemeSelection? initial = null)
    {
        Catalog = catalog;
        _store = store ?? NullThemePreferenceStore.Instance;
        var selection = catalog.Normalize(initial ?? catalog.DefaultSelection, out var diagnostics);
        LastDiagnostics = diagnostics;
        _state = new ThemeState
        {
            Selection = selection,
            EffectiveSelection = catalog.ResolveSystemValues(selection, SystemPreferences.Unknown),
            Overrides = ImmutableSortedDictionary<string, SourceNode>.Empty.WithComparers(StringComparer.Ordinal),
            SystemPreferences = SystemPreferences.Unknown,
        };
    }

    public ThemeCatalog Catalog { get; }
    public ThemeState State => _state;
    /// <summary>Diagnostics of the most recent operation (for example an unknown persisted theme that fell back to the default).</summary>
    public IReadOnlyList<Diagnostic> LastDiagnostics { get; private set; }

    /// <summary>Raised once per change, or once per batch. Handlers run synchronously on the caller's thread.</summary>
    public event EventHandler<ThemeChangedEventArgs>? Changed;

    /// <summary>Loads the persisted preference, if any. Unknown or invalid values fall back to defaults.</summary>
    public async Task<bool> LoadPreferenceAsync(CancellationToken cancellationToken = default)
    {
        ThemePreference? preference;
        try
        {
            preference = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage can be unavailable (privacy mode, disconnected circuit); the theme keeps working without it.
            return false;
        }
        if (preference is null) return false;
        Apply(new ThemeSelection(preference.ThemeId, preference.Axes), _state.Overrides, ThemeChangeKind.Theme | ThemeChangeKind.Axes, persist: false);
        return true;
    }

    public bool TrySetTheme(string themeId)
    {
        if (Catalog.FindTheme(themeId) is null)
        {
            LastDiagnostics = [new Diagnostic(DiagnosticCatalog.ThemeNotFound.Id, DiagnosticSeverity.Error, $"Theme '{themeId}' is not registered.", actual: themeId)];
            return false;
        }
        Apply(_state.Selection.WithTheme(themeId), _state.Overrides, ThemeChangeKind.Theme);
        return true;
    }

    public void SetTheme(string themeId)
    {
        if (!TrySetTheme(themeId)) throw new ArgumentException($"Theme '{themeId}' is not registered.", nameof(themeId));
    }

    public bool TrySetAxis(string axis, string value)
    {
        var definition = Catalog.FindAxis(axis);
        if (definition is null || !definition.Accepts(value))
        {
            LastDiagnostics = [new Diagnostic(DiagnosticCatalog.InvalidThemeSelection.Id, DiagnosticSeverity.Error, $"'{value}' is not a valid value for axis '{axis}'.", actual: value)];
            return false;
        }
        Apply(_state.Selection.WithAxis(axis, value), _state.Overrides, ThemeChangeKind.Axes);
        return true;
    }

    public void SetAxis(string axis, string value)
    {
        if (!TrySetAxis(axis, value)) throw new ArgumentException($"'{value}' is not a valid value for axis '{axis}'.", nameof(value));
    }

    /// <summary>Overrides a token value at runtime (DTCG JSON value). The override is validated when the theme is resolved.</summary>
    public void SetOverride(string tokenPath, SourceNode value)
    {
        if (TokenPath.Parse(tokenPath).Segments.Any(s => s.Length == 0 || (s.StartsWith('$') && s != TokenPath.RootTokenName)))
            throw new ArgumentException($"'{tokenPath}' is not a valid token path.", nameof(tokenPath));
        Apply(_state.Selection, _state.Overrides.SetItem(tokenPath, value), ThemeChangeKind.Overrides, persist: false);
    }

    public void RemoveOverride(string tokenPath)
    {
        if (_state.Overrides.ContainsKey(tokenPath)) Apply(_state.Selection, _state.Overrides.Remove(tokenPath), ThemeChangeKind.Overrides, persist: false);
    }

    public void ClearOverrides()
    {
        if (!_state.Overrides.IsEmpty) Apply(_state.Selection, _state.Overrides.Clear(), ThemeChangeKind.Overrides, persist: false);
    }

    /// <summary>Restores the default theme and axes and removes overrides. Persisted preferences are cleared.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Apply(Catalog.DefaultSelection, _state.Overrides.Clear(), ThemeChangeKind.Reset, persist: false);
        try { await _store.ClearAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* storage unavailable */ }
    }

    /// <summary>Called by browser adapters when operating-system preferences are known or change.</summary>
    public void UpdateSystemPreferences(SystemPreferences preferences)
    {
        if (preferences == _state.SystemPreferences) return;
        var previous = _state;
        _state = _state with { SystemPreferences = preferences, EffectiveSelection = Catalog.ResolveSystemValues(_state.Selection, preferences) };
        Notify(previous, ThemeChangeKind.SystemPreferences);
    }

    /// <summary>Groups several changes into a single <see cref="Changed"/> notification.</summary>
    public IDisposable BeginBatch()
    {
        if (_batchDepth++ == 0)
        {
            _batchStart = _state;
            _batchKind = ThemeChangeKind.None;
        }
        return new BatchScope(this);
    }

    /// <summary>Effective tokens for the current state, including overrides.</summary>
    public ThemeResolution Resolve(CancellationToken cancellationToken = default)
        => Catalog.Resolve(_state.EffectiveSelection, _state.OverridesTree, cancellationToken);

    /// <summary>The persisted preference corresponding to the current state.</summary>
    public ThemePreference ToPreference() => new(_state.Selection.ThemeId, _state.Selection.Axes);

    /// <summary>Raised after persisting fails; the change itself has been applied.</summary>
    public event EventHandler<Exception>? PersistenceFailed;

    private void Apply(ThemeSelection selection, ImmutableSortedDictionary<string, SourceNode> overrides, ThemeChangeKind kind, bool persist = true)
    {
        var normalized = Catalog.Normalize(selection, out var diagnostics);
        LastDiagnostics = diagnostics;
        if (normalized.Equals(_state.Selection) && overrides == _state.Overrides && kind != ThemeChangeKind.Reset) return;
        var previous = _state;
        _state = _state with
        {
            Selection = normalized,
            EffectiveSelection = Catalog.ResolveSystemValues(normalized, _state.SystemPreferences),
            Overrides = overrides,
        };
        Notify(previous, kind);
        if (persist) _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _store.SaveAsync(ToPreference()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PersistenceFailed?.Invoke(this, ex);
        }
    }

    private void Notify(ThemeState previous, ThemeChangeKind kind)
    {
        if (_batchDepth > 0)
        {
            _batchKind |= kind;
            return;
        }
        Changed?.Invoke(this, new ThemeChangedEventArgs(previous, _state, kind));
    }

    private void EndBatch()
    {
        if (--_batchDepth > 0) return;
        var start = _batchStart!;
        _batchStart = null;
        if (_batchKind != ThemeChangeKind.None && start != _state) Changed?.Invoke(this, new ThemeChangedEventArgs(start, _state, _batchKind));
    }

    private sealed class BatchScope(ThemeManager manager) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            manager.EndBatch();
        }
    }
}
