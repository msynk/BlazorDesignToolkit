using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorDesignToolkit.Primitives;

public enum Orientation
{
    Horizontal,
    Vertical,
}

public enum TextDirection
{
    Ltr,
    Rtl,
}

/// <summary>Generates document-unique element ids. Pass your own <c>Id</c> to any primitive to control it.</summary>
public static class Ids
{
    private static long s_next;

    public static string Next(string prefix) => $"bdt-{prefix}-{Interlocked.Increment(ref s_next)}";
}

/// <summary>Navigation intents derived from keys, independent of orientation and direction.</summary>
public enum NavigationIntent
{
    None,
    Next,
    Previous,
    First,
    Last,
}

/// <summary>Pure keyboard-navigation logic shared by composite widgets (WAI-ARIA Authoring Practices).</summary>
public static class KeyboardNavigation
{
    /// <summary>Maps a key to an intent. Horizontal widgets use Left/Right (mirrored in RTL), vertical widgets Up/Down.</summary>
    public static NavigationIntent Intent(string key, Orientation orientation, TextDirection direction = TextDirection.Ltr, bool both = false)
    {
        var rtl = direction == TextDirection.Rtl;
        return key switch
        {
            "Home" => NavigationIntent.First,
            "End" => NavigationIntent.Last,
            "ArrowRight" when orientation == Orientation.Horizontal || both => rtl ? NavigationIntent.Previous : NavigationIntent.Next,
            "ArrowLeft" when orientation == Orientation.Horizontal || both => rtl ? NavigationIntent.Next : NavigationIntent.Previous,
            "ArrowDown" when orientation == Orientation.Vertical || both => NavigationIntent.Next,
            "ArrowUp" when orientation == Orientation.Vertical || both => NavigationIntent.Previous,
            _ => NavigationIntent.None,
        };
    }

    /// <summary>
    /// Moves from <paramref name="current"/> according to <paramref name="intent"/>, skipping disabled items.
    /// Returns -1 when no enabled item exists, and <paramref name="current"/> when movement is not possible.
    /// </summary>
    public static int Move(int current, int count, Func<int, bool> isDisabled, NavigationIntent intent, bool loop = true)
    {
        if (count == 0) return -1;
        int Scan(int start, int step)
        {
            for (int i = start, n = 0; n < count; n++, i += step)
            {
                if (i < 0 || i >= count)
                {
                    if (!loop) return -1;
                    i = (i + count) % count;
                }
                if (!isDisabled(i)) return i;
            }
            return -1;
        }
        switch (intent)
        {
            case NavigationIntent.First: return Scan(0, 1);
            case NavigationIntent.Last: return Scan(count - 1, -1);
            case NavigationIntent.Next:
                {
                    if (current < 0) return Scan(0, 1);
                    var next = Scan(current + 1, 1);
                    return next < 0 ? current : next;
                }
            case NavigationIntent.Previous:
                {
                    if (current < 0) return Scan(count - 1, -1);
                    var prev = Scan(current - 1, -1);
                    return prev < 0 ? current : prev;
                }
            default: return current;
        }
    }

    public static bool IsPrintable(KeyboardEventArgs e) => e.Key.Length == 1 && !e.CtrlKey && !e.MetaKey && !e.AltKey;
}

/// <summary>
/// Type-to-select: printable keys typed within <see cref="Timeout"/> form a search string; the next item whose label
/// starts with it (case-insensitive) becomes active. Repeating one character cycles through items starting with it.
/// </summary>
public sealed class Typeahead(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private string _buffer = "";
    private DateTimeOffset _last;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(500);

    public int Search(char typed, int current, IReadOnlyList<string> labels, Func<int, bool> isDisabled)
    {
        var now = _time.GetUtcNow();
        _buffer = now - _last > Timeout ? typed.ToString() : _buffer + typed;
        _last = now;
        var query = _buffer.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(_buffer[0])) ? _buffer[..1] : _buffer;
        var start = query.Length == 1 ? current + 1 : Math.Max(current, 0);
        for (var n = 0; n < labels.Count; n++)
        {
            var i = (start + n) % labels.Count;
            if (!isDisabled(i) && labels[i].StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return i;
        }
        return current;
    }

    public void Reset() => _buffer = "";
}

/// <summary>
/// Lazily imported <c>bdt-primitives.js</c> module. Calls before the first interactive render (prerendering) and after a
/// server circuit disconnects are ignored, so primitives degrade gracefully.
/// </summary>
public sealed class PrimitivesJs(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./_content/BlazorDesignToolkit.Primitives/bdt-primitives.js";
    private Task<IJSObjectReference>? _module;
    private readonly List<int> _handles = [];
    private bool _disposed;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask<int> TrackAsync(string identifier, params object?[] args)
    {
        var handle = await InvokeAsync<int>(identifier, args);
        if (handle != 0) _handles.Add(handle);
        return handle;
    }

    public async ValueTask ReleaseAsync(int handle)
    {
        if (handle == 0) return;
        _handles.Remove(handle);
        await InvokeVoidAsync("release", handle);
    }

    public ValueTask InvokeVoidAsync(string identifier, params object?[] args) => InvokeCore<object>(identifier, args, isVoid: true).AsVoid();

    public ValueTask<T?> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>(string identifier, params object?[] args)
        => InvokeCore<T>(identifier, args, isVoid: false);

    private async ValueTask<T?> InvokeCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>(string identifier, object?[] args, bool isVoid)
    {
        if (_disposed) return default;
        try
        {
            var module = await Module;
            if (isVoid)
            {
                await module.InvokeVoidAsync(identifier, args);
                return default;
            }
            return await module.InvokeAsync<T>(identifier, args);
        }
        catch (JSDisconnectedException) { return default; }
        catch (InvalidOperationException) { return default; } // JS interop unavailable (prerendering)
        catch (TaskCanceledException) { return default; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        foreach (var handle in _handles.ToArray()) await InvokeVoidAsync("release", handle);
        _handles.Clear();
        _disposed = true;
        if (_module is null) return;
        try { await (await _module).DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
        catch (TaskCanceledException) { }
    }
}

internal static class ValueTaskExtensions
{
    public static async ValueTask AsVoid<T>(this ValueTask<T> task) => await task;
}

/// <summary>Base class: consumer attributes, element ids, JS access and disposal.</summary>
public abstract class PrimitiveBase : ComponentBase, IAsyncDisposable
{
    private PrimitivesJs? _js;

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>Built-in texts: the registered <see cref="PrimitiveStrings"/> or the English defaults.</summary>
    protected PrimitiveStrings Strings => (PrimitiveStrings?)Services.GetService(typeof(PrimitiveStrings)) ?? PrimitiveStrings.Default;

    /// <summary>Additional attributes (class, style, data-*, aria-label…) splatted on the primitive's main element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected PrimitivesJs Js => _js ??= new PrimitivesJs(JSRuntime);

    private readonly HashSet<string> _provided = new(StringComparer.Ordinal);

    public override Task SetParametersAsync(ParameterView parameters)
    {
        _provided.Clear();
        foreach (var parameter in parameters) _provided.Add(parameter.Name);
        return base.SetParametersAsync(parameters);
    }

    /// <summary>True when the parent passed <paramref name="parameterName"/> in its latest render (controlled state).</summary>
    protected bool IsProvided(string parameterName) => _provided.Contains(parameterName);

    protected bool IsDisposed { get; private set; }

    /// <summary>Consumer attributes without the keys the primitive controls (state and relationship attributes always win).</summary>
    protected IReadOnlyDictionary<string, object>? Attributes(params string[] reserved)
    {
        if (AdditionalAttributes is null || reserved.Length == 0) return AdditionalAttributes;
        return AdditionalAttributes.Where(a => !reserved.Contains(a.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The consumer-provided id attribute, if any.</summary>
    protected string? ProvidedId => AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("id", out var id) ? id?.ToString() : null;

    private readonly List<PrimitiveContext> _tracked = [];

    /// <summary>Re-renders this primitive when a parent context (form field, group, tabs, menu…) changes state.</summary>
    protected void Track(PrimitiveContext? context)
    {
        if (context is null || _tracked.Contains(context)) return;
        _tracked.Add(context);
        context.StateChanged += OnContextChanged;
    }

    protected void TrackField(FormFieldContext? field) => Track(field);

    private void OnContextChanged()
    {
        if (!IsDisposed) _ = InvokeAsync(StateHasChanged);
    }

    public virtual async ValueTask DisposeAsync()
    {
        IsDisposed = true;
        foreach (var c in _tracked) c.StateChanged -= OnContextChanged;
        _tracked.Clear();
        if (_js is not null) await _js.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Base for state shared by a composite primitive with its parts; parts re-render when it changes.</summary>
public abstract class PrimitiveContext
{
    public event Action? StateChanged;

    public void NotifyStateChanged() => StateChanged?.Invoke();
}

/// <summary>
/// A JavaScript resource opened in OnAfterRenderAsync (an overlay, a focus trap). The open call is kept as a task so that a
/// close requested before it has returned its handle (OnAfterRenderAsync calls can overlap) still releases the resource.
/// </summary>
public sealed class JsResource
{
    private Task<int>? _opening;

    public bool IsOpen => _opening is not null;

    public async Task OpenAsync(Func<ValueTask<int>> open)
    {
        _opening = open().AsTask();
        await _opening;
    }

    public async Task CloseAsync(PrimitivesJs js)
    {
        if (_opening is not { } opening) return;
        _opening = null;
        await js.ReleaseAsync(await opening);
    }
}

/// <summary>
/// State for a bindable parameter (<c>Value</c> / <c>ValueChanged</c> / <c>DefaultValue</c>), following Blazor input
/// conventions: the component keeps its own current value, user changes update it and raise <c>ValueChanged</c>, and a
/// new value passed by the parent (for example through <c>@bind-Value</c>) replaces it.
/// <para>
/// A parent that passes the value and handles the change controls it (as with React's controlled inputs): it accepts a
/// change by passing the new value once its change handler has completed, otherwise the value reverts to the one it passes.
/// Renders while the handler is still running (Blazor renders an asynchronous handler's component before its first await)
/// do not decide, so the value does not jump back while the parent is still saving. A controlled parent must therefore
/// re-render after the change: when it accepts without re-rendering (<c>ShouldRender</c> returns false, or the handler
/// belongs to a component that does not render this one), the value reverts until the parent's next render passes it.
/// </para>
/// </summary>
public sealed class Controllable<T>
{
    private T _value;
    private T _lastParameter = default!;
    private bool _initialized;
    private bool _synced;
    private bool _pending;
    private bool _handlerRunning;
    private bool _provided;
    private int _version;

    public Controllable(T initial) => _value = initial;

    public T Value => _value;

    /// <summary>Current value (the parameters are accepted for call-site clarity; state is synchronized in <see cref="Sync"/>).</summary>
    public T Get(T parameter, bool controlled) => _value;

    public void Initialize(T defaultValue)
    {
        if (_initialized) return;
        _value = defaultValue;
        _initialized = true;
    }

    /// <summary>
    /// Call from OnParametersSet: adopts the parameter when the parent passes a different value than before. After a change
    /// raised through <see cref="SetAsync"/> on a controlled component, a parent render that still passes the old value once
    /// the change handler has completed means the parent did not accept it, and the value reverts. Returns true on a revert.
    /// </summary>
    public bool Sync(T parameter, bool provided = true)
    {
        _provided = provided;
        if (!_synced)
        {
            _lastParameter = parameter;
            _synced = true;
            return false;
        }
        if (!EqualityComparer<T>.Default.Equals(parameter, _lastParameter))
        {
            _lastParameter = parameter;
            _value = parameter;
            _pending = false;
            return false;
        }
        // A render while the change handler is still running (for example before its first await) does not decide: the
        // value would jump back while the parent is still saving. SetAsync decides once the handler has completed.
        if (!_pending || _handlerRunning) return false;
        return Revert();
    }

    /// <summary>
    /// Sets the value and raises <paramref name="changed"/>. When the parent controls the value (it passes the parameter and
    /// handles the change), the parent's answer is the value it renders with during or right after the handler (see the class
    /// remarks): a change it did not accept is reverted. Returns true when the change was reverted.
    /// </summary>
    public async Task<bool> SetAsync(T value, bool controlled, EventCallback<T> changed)
    {
        _value = value;
        // A newer change (for example the next pointer move) supersedes this one, including its acceptance check.
        var version = ++_version;
        // Only a parent that passes the value (not just a change handler) controls it; Default… + …Changed stays uncontrolled.
        _pending = controlled && _provided && _synced;
        _handlerRunning = _pending;
        try
        {
            // Completes after the handler, including its asynchronous part and the parent's render that follows it.
            if (changed.HasDelegate) await changed.InvokeAsync(value);
        }
        finally
        {
            if (version == _version) _handlerRunning = false;
        }
        if (!_pending || version != _version) return false;
        // Inside an event batch the parent renders after the handler returns; yield once so that render (and our
        // OnParametersSet, which adopts a new value or reverts) runs first.
        await Task.Yield();
        if (!_pending || version != _version) return false;
        // The parameter did not change. Blazor does not even pass parameters to a child whose parameters are all unchanged
        // (a bool and the same handler), so this cannot be told apart from a parent that did not re-render: revert.
        return Revert();
    }

    private bool Revert()
    {
        _pending = false;
        if (EqualityComparer<T>.Default.Equals(_value, _lastParameter)) return false;
        _value = _lastParameter;
        return true;
    }
}
