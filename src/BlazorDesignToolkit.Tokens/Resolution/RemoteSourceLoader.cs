using System.Net;
using BlazorDesignToolkit.Tokens.Diagnostics;
using BlazorDesignToolkit.Tokens.Json;

namespace BlazorDesignToolkit.Tokens.Resolution;

/// <summary>Restrictions for <see cref="HttpResolverSourceLoader"/>. Remote loading is off unless prefixes are listed.</summary>
public sealed record RemoteSourceOptions
{
    /// <summary>
    /// URL prefixes that may be fetched, e.g. <c>https://tokens.example.com/design/</c>. A reference is allowed only when it
    /// starts with one of them (compared after normalization, scheme and host case-insensitively). Only HTTPS is accepted.
    /// </summary>
    public IReadOnlyList<Uri> AllowedPrefixes { get; init; } = [];

    /// <summary>Upper limit of <see cref="Timeout"/>.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(1);

    /// <summary>Time allowed for one request, including reading the body. Default 30 seconds, at most <see cref="MaxTimeout"/>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Parse limits for fetched documents; <see cref="JsonParseOptions.MaxInputBytes"/> also limits the download size.</summary>
    public JsonParseOptions ParseOptions { get; init; } = JsonParseOptions.Default;

    /// <summary>Validates the options; returns an error message or null.</summary>
    public string? Validate()
    {
        foreach (var prefix in AllowedPrefixes)
        {
            if (!prefix.IsAbsoluteUri || prefix.Scheme != Uri.UriSchemeHttps) return $"Remote prefix '{prefix}' must be an absolute https:// URL.";
            if (!string.IsNullOrEmpty(prefix.Query) || !string.IsNullOrEmpty(prefix.Fragment)) return $"Remote prefix '{prefix}' must not contain a query or fragment.";
        }
        if (Timeout <= TimeSpan.Zero) return "The remote timeout must be positive.";
        return Timeout > MaxTimeout ? $"The remote timeout must be at most {MaxTimeout.TotalSeconds:0} seconds." : null;
    }

    internal bool IsAllowed(Uri uri)
        => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
           && AllowedPrefixes.Any(p => string.Equals(p.GetLeftPart(UriPartial.Authority), uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                                       && uri.AbsolutePath.StartsWith(p.AbsolutePath, StringComparison.Ordinal));
}

/// <summary>
/// Opt-in loader for remote <c>$ref</c> targets (DTCG resolver sources and theme layers) over HTTPS. Only URLs under an
/// allow-listed prefix are fetched; redirects are followed only when the target is allow-listed too; downloads are limited
/// in size and time; credentials in URLs are rejected; no cookies or authentication are sent unless the supplied
/// <see cref="HttpClient"/> adds them. Relative references are delegated to <paramref name="local"/> (typically a
/// <see cref="FileSystemResolverSourceLoader"/>).
/// </summary>
/// <remarks>
/// Fetched content is parsed with the same limits as local files and never executed. Configure the
/// <see cref="HttpClient"/> handler with <c>AllowAutoRedirect = false</c> (as <see cref="CreateHttpClient"/> does) so that every
/// redirect hop is checked against the allow-list.
/// </remarks>
public sealed class HttpResolverSourceLoader(HttpClient client, RemoteSourceOptions options, IResolverSourceLoader? local = null) : IResolverSourceLoader
{
    private const int MaxRedirects = 5;

    public RemoteSourceOptions Options { get; } = options;

    /// <summary>An <see cref="HttpClient"/> that does not follow redirects automatically, so each hop is validated.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
    });

    public async Task<SourceParseResult> LoadAsync(string reference, string? baseDirectory, CancellationToken cancellationToken)
    {
        if (!LooksRemote(reference))
        {
            if (local is not null) return await local.LoadAsync(reference, baseDirectory, cancellationToken).ConfigureAwait(false);
            return Fail(DiagnosticCatalog.FileNotFound, $"Local reference '{reference}' cannot be loaded: no local loader is configured.", reference);
        }
        if (!Uri.TryCreate(reference, UriKind.Absolute, out var uri)) return Fail(DiagnosticCatalog.UnsupportedConstruct, $"'{reference}' is not a valid URL.", reference);
        if (!Options.IsAllowed(uri))
        {
            return Fail(DiagnosticCatalog.UnsupportedConstruct,
                $"Remote reference '{reference}' is not allowed. Only https:// URLs under an allow-listed prefix are fetched.", reference,
                "Add the URL prefix to the remote allow-list (CLI: \"remote\": { \"allow\": [ … ] }) or download the file.");
        }

        if (Options.Validate() is { } invalid) return Fail(DiagnosticCatalog.InvalidConfiguration, invalid, reference);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Options.Timeout);
        try
        {
            var current = uri;
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (hop >= MaxRedirects) return Fail(DiagnosticCatalog.FileNotFound, $"Too many redirects while fetching '{reference}'.", reference);
                    if (response.Headers.Location is not { } location) return Fail(DiagnosticCatalog.FileNotFound, $"Redirect without a location while fetching '{reference}'.", reference);
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!Options.IsAllowed(next))
                    {
                        return Fail(DiagnosticCatalog.UnsupportedConstruct, $"'{reference}' redirects to '{next}', which is not allow-listed.", next.ToString());
                    }
                    current = next;
                    continue;
                }
                // A handler that followed redirects itself: check where the response came from.
                if (response.RequestMessage?.RequestUri is { } final && final != current && !Options.IsAllowed(final))
                {
                    return Fail(DiagnosticCatalog.UnsupportedConstruct, $"'{reference}' was redirected to '{final}', which is not allow-listed.", final.ToString());
                }
                if (!response.IsSuccessStatusCode)
                {
                    return Fail(DiagnosticCatalog.FileNotFound, $"Fetching '{reference}' failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", reference);
                }
                var limit = Options.ParseOptions.MaxInputBytes;
                if (response.Content.Headers.ContentLength is { } length && length > limit)
                {
                    return Fail(DiagnosticCatalog.InputTooLarge, $"'{reference}' is {length} bytes; the limit is {limit} bytes.", reference);
                }
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > limit) return Fail(DiagnosticCatalog.InputTooLarge, $"'{reference}' exceeds the limit of {limit} bytes.", reference);
                    buffer.Write(chunk, 0, read);
                }
                var text = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                return SourceJsonParser.Parse(text, Options.ParseOptions with { SourceName = reference }, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(DiagnosticCatalog.FileNotFound, $"Fetching '{reference}' timed out after {Options.Timeout.TotalSeconds:0.#} s.", reference);
        }
        catch (HttpRequestException ex)
        {
            return Fail(DiagnosticCatalog.FileNotFound, $"Fetching '{reference}' failed: {ex.Message}", reference);
        }
        catch (IOException ex)
        {
            // For example HttpIOException: the connection was reset while the body was being read.
            return Fail(DiagnosticCatalog.FileNotFound, $"Fetching '{reference}' failed while reading the response: {ex.Message}", reference);
        }
    }

    internal static bool LooksRemote(string reference) => reference.Contains("://", StringComparison.Ordinal);

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static SourceParseResult Fail(DiagnosticDescriptor descriptor, string message, string actual, string? remediation = null)
    {
        var bag = new DiagnosticBag();
        bag.Report(descriptor, message, actual: actual, remediation: remediation);
        return new SourceParseResult(null, bag.Items);
    }
}
