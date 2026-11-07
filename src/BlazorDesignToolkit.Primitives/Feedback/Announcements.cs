using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorDesignToolkit.Primitives;

public enum Politeness
{
    /// <summary>Announced when the user is idle (aria-live="polite", role="status").</summary>
    Polite,
    /// <summary>Interrupts the user (aria-live="assertive", role="alert"). Use only for urgent, time-sensitive messages.</summary>
    Assertive,
}

/// <summary>
/// Sends messages to screen readers through a <see cref="LiveRegion"/> rendered once in the layout.
/// Use for status changes that are not otherwise conveyed (for example "3 results found", "Saved").
/// </summary>
public sealed class LiveAnnouncer
{
    public event Action<string, Politeness>? Announced;

    public void Announce(string message, Politeness politeness = Politeness.Polite)
    {
        if (!string.IsNullOrWhiteSpace(message)) Announced?.Invoke(message, politeness);
    }
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record ToastOptions
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public ToastKind Kind { get; init; } = ToastKind.Info;
    /// <summary>Time before auto-dismissal; null keeps the toast until dismissed. Errors default to persistent.</summary>
    public TimeSpan? Duration { get; init; } = TimeSpan.FromSeconds(6);
    public string? ActionLabel { get; init; }
    public Func<Task>? Action { get; init; }
}

public sealed class Toast
{
    internal Toast(long id, ToastOptions options)
    {
        Id = id;
        Options = options;
        Remaining = options.Duration;
    }

    public long Id { get; }
    public ToastOptions Options { get; }
    internal TimeSpan? Remaining { get; set; }
    internal DateTimeOffset? StartedAt { get; set; }
    internal CancellationTokenSource? Timer { get; set; }
}

/// <summary>Queue of toast notifications rendered by <see cref="ToastRegion"/>.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];
    private long _next;

    public IReadOnlyList<Toast> Toasts => _toasts;
    public event Action? Changed;

    public long Show(ToastOptions options)
    {
        if (options.Kind == ToastKind.Error && options.Duration == TimeSpan.FromSeconds(6)) options = options with { Duration = null };
        var toast = new Toast(Interlocked.Increment(ref _next), options);
        _toasts.Add(toast);
        Changed?.Invoke();
        return toast.Id;
    }

    public long Show(string message, ToastKind kind = ToastKind.Info) => Show(new ToastOptions { Message = message, Kind = kind });

    public void Dismiss(long id)
    {
        var toast = _toasts.Find(t => t.Id == id);
        if (toast is null) return;
        toast.Timer?.Cancel();
        _toasts.Remove(toast);
        Changed?.Invoke();
    }

    public void DismissAll()
    {
        foreach (var t in _toasts) t.Timer?.Cancel();
        _toasts.Clear();
        Changed?.Invoke();
    }
}

public static class PrimitivesServiceCollectionExtensions
{
    /// <summary>Registers the scoped services used by <see cref="LiveRegion"/> and <see cref="ToastRegion"/>.</summary>
    public static IServiceCollection AddHeadlessPrimitives(this IServiceCollection services)
    {
        services.TryAddScoped<LiveAnnouncer>();
        services.TryAddScoped<ToastService>();
        return services;
    }
}
