using System.Text;
using System.Text.Json;
using BlazorDesignToolkit.Tokens.Diagnostics;

namespace BlazorDesignToolkit.Tokens.Json;

/// <summary>Resource limits and syntax options for reading untrusted JSON.</summary>
public sealed record JsonParseOptions
{
    public static JsonParseOptions Default { get; } = new();

    /// <summary>Maximum accepted input size in bytes (UTF-8). Default 16 MiB.</summary>
    public long MaxInputBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum nesting depth of objects and arrays. Default 64; must be between 1 and 1000.</summary>
    public int MaxDepth { get; init; } = 64;

    /// <summary>
    /// Accept JavaScript-style comments and trailing commas. DTCG documents must be standard JSON,
    /// so this is off by default; enable it only for hand-edited inputs you convert immediately.
    /// </summary>
    public bool AllowCommentsAndTrailingCommas { get; init; }

    /// <summary>Name reported in source locations (typically the file path).</summary>
    public string? SourceName { get; init; }
}

public sealed class SourceParseResult(SourceNode? root, IReadOnlyList<Diagnostic> diagnostics)
{
    /// <summary>The parsed node, or null when the input is not valid JSON or exceeds a limit.</summary>
    public SourceNode? Root { get; } = root;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Reads JSON into <see cref="SourceNode"/> trees with line/column information, duplicate-key detection,
/// size and depth limits, and cancellation. Never executes or evaluates input.
/// </summary>
public static class SourceJsonParser
{
    public static SourceParseResult Parse(string json, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        options ??= JsonParseOptions.Default;
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > options.MaxInputBytes) return TooLarge(byteCount, options);
        return Parse(Encoding.UTF8.GetBytes(json), options, cancellationToken);
    }

    public static async Task<SourceParseResult> ParseAsync(Stream stream, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= JsonParseOptions.Default;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > options.MaxInputBytes) return TooLarge(buffer.Length + read, options, atLeast: true);
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options, cancellationToken);
    }

    public static async Task<SourceParseResult> ParseFileAsync(string path, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= JsonParseOptions.Default;
        if (options.SourceName is null) options = options with { SourceName = path };
        var info = new FileInfo(path);
        if (info.Exists && info.Length > options.MaxInputBytes) return TooLarge(info.Length, options);
        await using var stream = File.OpenRead(path);
        return await ParseAsync(stream, options, cancellationToken).ConfigureAwait(false);
    }

    public static SourceParseResult Parse(ReadOnlySpan<byte> utf8, JsonParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= JsonParseOptions.Default;
        if (options.MaxDepth is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be between 1 and 1000.");
        if (utf8.Length > options.MaxInputBytes) return TooLarge(utf8.Length, options);

        // Skip a UTF-8 byte order mark.
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) utf8 = utf8[3..];

        var lines = new LineMap(utf8, options.SourceName);
        var bag = new DiagnosticBag();
        var readerOptions = new JsonReaderOptions
        {
            MaxDepth = options.MaxDepth,
            CommentHandling = options.AllowCommentsAndTrailingCommas ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            AllowTrailingCommas = options.AllowCommentsAndTrailingCommas,
        };
        var reader = new Utf8JsonReader(utf8, readerOptions);
        var state = new ParseState(lines, bag, cancellationToken);
        try
        {
            if (!reader.Read())
            {
                bag.Report(DiagnosticCatalog.InvalidJson, "The input is empty.", location: lines.At(0));
                return new SourceParseResult(null, bag.ToSortedList());
            }
            var root = ReadValue(ref reader, state);
            if (reader.Read())
            {
                bag.Report(DiagnosticCatalog.InvalidJson, "Unexpected content after the root JSON value.", location: lines.At((int)reader.TokenStartIndex));
                return new SourceParseResult(null, bag.ToSortedList());
            }
            return new SourceParseResult(root, bag.ToSortedList());
        }
        catch (JsonException ex)
        {
            var location = new SourceLocation(options.SourceName, (int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1);
            var isDepth = ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase);
            if (isDepth)
            {
                bag.Report(DiagnosticCatalog.NestingTooDeep, $"The document exceeds the maximum nesting depth of {options.MaxDepth}.",
                    location: location, expected: $"<= {options.MaxDepth}");
            }
            else
            {
                bag.Report(DiagnosticCatalog.InvalidJson, $"Invalid JSON: {TrimReaderMessage(ex.Message)}", location: location);
            }
            return new SourceParseResult(null, bag.ToSortedList());
        }
    }

    private static string TrimReaderMessage(string message)
    {
        // System.Text.Json appends " LineNumber: x | BytePositionInLine: y." which we report structurally instead.
        var i = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return i > 0 ? message[..i].TrimEnd() : message;
    }

    private static SourceParseResult TooLarge(long size, JsonParseOptions options, bool atLeast = false)
    {
        var bag = new DiagnosticBag();
        bag.Report(DiagnosticCatalog.InputTooLarge,
            $"Input of {(atLeast ? "at least " : "")}{size} bytes exceeds the limit of {options.MaxInputBytes} bytes.",
            location: options.SourceName is null ? null : new SourceLocation(options.SourceName, 1, 1),
            expected: $"<= {options.MaxInputBytes} bytes", actual: $"{size} bytes");
        return new SourceParseResult(null, bag.ToSortedList());
    }

    private sealed class ParseState(LineMap lines, DiagnosticBag bag, CancellationToken cancellationToken)
    {
        private int _counter;
        public LineMap Lines { get; } = lines;
        public DiagnosticBag Bag { get; } = bag;

        public void Tick()
        {
            if ((++_counter & 0x3FF) == 0) cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static SourceNode ReadValue(ref Utf8JsonReader reader, ParseState state)
    {
        state.Tick();
        var location = state.Lines.At((int)reader.TokenStartIndex);
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                {
                    var props = new List<SourceProperty>();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    while (true)
                    {
                        if (!reader.Read()) throw new JsonException("Unexpected end of input inside an object.");
                        if (reader.TokenType == JsonTokenType.EndObject) break;
                        var nameLocation = state.Lines.At((int)reader.TokenStartIndex);
                        var name = reader.GetString()!;
                        if (!seen.Add(name))
                        {
                            state.Bag.Report(DiagnosticCatalog.DuplicateKey, $"Duplicate property name '{name}'. The last occurrence is used.",
                                location: nameLocation, actual: name);
                        }
                        if (!reader.Read()) throw new JsonException("Unexpected end of input after a property name.");
                        props.Add(new SourceProperty(name, ReadValue(ref reader, state), nameLocation));
                    }
                    return new SourceObject(props, location);
                }
            case JsonTokenType.StartArray:
                {
                    var items = new List<SourceNode>();
                    while (true)
                    {
                        if (!reader.Read()) throw new JsonException("Unexpected end of input inside an array.");
                        if (reader.TokenType == JsonTokenType.EndArray) break;
                        items.Add(ReadValue(ref reader, state));
                    }
                    return new SourceArray(items, location);
                }
            case JsonTokenType.String:
                return new SourceString(reader.GetString()!, location);
            case JsonTokenType.Number:
                {
                    var raw = Encoding.UTF8.GetString(reader.ValueSpan);
                    if (!reader.TryGetDouble(out var value) || double.IsInfinity(value) || double.IsNaN(value))
                    {
                        throw new JsonException($"The number '{raw}' is outside the supported range.");
                    }
                    return new SourceNumber(value, raw, location);
                }
            case JsonTokenType.True:
                return new SourceBoolean(true, location);
            case JsonTokenType.False:
                return new SourceBoolean(false, location);
            case JsonTokenType.Null:
                return new SourceNull(location);
            default:
                throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }

    /// <summary>Maps byte offsets to 1-based line/column (columns count Unicode scalar values).</summary>
    private sealed class LineMap
    {
        private readonly byte[] _bytes;
        private readonly List<int> _lineStarts = [0];
        private readonly string? _source;
        // The parser asks for increasing offsets. Counting on from the previous position keeps minified (single-line)
        // documents linear; counting from the start of the line every time made them quadratic.
        private int _lastLine = -1;
        private int _lastOffset;
        private int _lastColumn;

        public LineMap(ReadOnlySpan<byte> utf8, string? source)
        {
            _bytes = utf8.ToArray();
            _source = source;
            for (var i = 0; i < _bytes.Length; i++)
            {
                if (_bytes[i] == (byte)'\n') _lineStarts.Add(i + 1);
            }
        }

        public SourceLocation At(int offset)
        {
            var line = _lineStarts.BinarySearch(offset);
            if (line < 0) line = ~line - 1;
            int i, column;
            if (line == _lastLine && offset >= _lastOffset)
            {
                i = _lastOffset;
                column = _lastColumn;
            }
            else
            {
                i = _lineStarts[line];
                column = 1;
            }
            for (; i < offset && i < _bytes.Length; i++)
            {
                if ((_bytes[i] & 0xC0) != 0x80) column++;
            }
            (_lastLine, _lastOffset, _lastColumn) = (line, i, column);
            return new SourceLocation(_source, line + 1, column);
        }
    }
}
