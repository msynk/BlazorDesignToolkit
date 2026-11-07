using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BlazorDesignToolkit.Tokens.Json;

/// <summary>Deterministic serializer for <see cref="SourceNode"/> trees: LF newlines, 2-space indentation, original property order.</summary>
public static class SourceJsonWriter
{
    public static JsonWriterOptions CreateOptions(bool indented) => new()
    {
        Indented = indented,
        IndentSize = 2,
        IndentCharacter = ' ',
        NewLine = "\n",
        // Token files are data files, not HTML; keep non-ASCII readable. Control characters and quotes are still escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(Utf8JsonWriter writer, SourceNode node)
    {
        switch (node)
        {
            case SourceObject o:
                writer.WriteStartObject();
                foreach (var p in o.Properties)
                {
                    writer.WritePropertyName(p.Name);
                    Write(writer, p.Value);
                }
                writer.WriteEndObject();
                break;
            case SourceArray a:
                writer.WriteStartArray();
                foreach (var item in a.Items) Write(writer, item);
                writer.WriteEndArray();
                break;
            case SourceString s:
                writer.WriteStringValue(s.Value);
                break;
            case SourceNumber n:
                // decimal keeps the author's scale ("1.50" stays "1.50") and indentation stays correct (unlike WriteRawValue).
                if (n.RawText is not null && decimal.TryParse(n.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dec)
                    && (double)dec == n.Value) writer.WriteNumberValue(dec);
                else writer.WriteNumberValue(n.Value);
                break;
            case SourceBoolean b:
                writer.WriteBooleanValue(b.Value);
                break;
            case SourceNull:
                writer.WriteNullValue();
                break;
            default:
                throw new NotSupportedException($"Unknown node type {node.GetType().Name}.");
        }
    }

    public static byte[] ToUtf8Bytes(SourceNode node, bool indented = true)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CreateOptions(indented)))
        {
            Write(writer, node);
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static string ToJsonString(SourceNode node, bool indented = false)
        => Encoding.UTF8.GetString(ToUtf8Bytes(node, indented));
}

/// <summary>RFC 6901 JSON Pointer helpers for same-document fragment references ("#/a/b").</summary>
public static class JsonPointer
{
    /// <summary>
    /// Parses a same-document pointer such as <c>#/colors/blue/$value</c>. The fragment is percent-decoded first
    /// (RFC 6901 §6, UTF-8), so <c>#/brand%20colors</c> and <c>#/brand colors</c> address the same group. A '%' that does
    /// not start a valid escape (two hexadecimal digits) is kept literally, because DTCG examples use raw characters in
    /// fragments and token names may contain '%'.
    /// </summary>
    public static bool TryParse(string pointer, out IReadOnlyList<string> segments, out string? error)
    {
        segments = [];
        error = null;
        if (!pointer.StartsWith('#'))
        {
            error = "Only same-document pointers starting with '#' are supported in token files.";
            return false;
        }
        if (!TryPercentDecode(pointer.AsSpan(1), out var body))
        {
            error = "The pointer contains percent-encoded bytes that are not valid UTF-8.";
            return false;
        }
        if (body.Length == 0)
        {
            error = "The pointer refers to the whole document.";
            return false;
        }
        if (body[0] != '/')
        {
            error = "A JSON Pointer fragment must start with '#/'.";
            return false;
        }
        var parts = body[1..].Split('/');
        var list = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            var sb = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '~')
                {
                    sb.Append(raw[i]);
                    continue;
                }
                if (i + 1 < raw.Length && raw[i + 1] == '0') sb.Append('~');
                else if (i + 1 < raw.Length && raw[i + 1] == '1') sb.Append('/');
                else
                {
                    error = $"Invalid escape sequence in pointer segment '{raw}'. Use '~0' for '~' and '~1' for '/'.";
                    return false;
                }
                i++;
            }
            list.Add(sb.ToString());
        }
        segments = list;
        return true;
    }

    /// <summary>Formats a pointer fragment. '~' and '/' are escaped as in RFC 6901 and '%' is percent-encoded so that the
    /// result parses back to the same segments; other characters are kept as written.</summary>
    public static string Format(IEnumerable<string> segments)
        => "#/" + string.Join('/', segments.Select(static s => s.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal).Replace("%", "%25", StringComparison.Ordinal)));

    private static bool TryPercentDecode(ReadOnlySpan<char> text, out string decoded)
    {
        decoded = text.ToString();
        if (text.IndexOf('%') < 0) return true;
        var bytes = new List<byte>(text.Length);
        Span<byte> utf8 = stackalloc byte[4];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length && IsHex(text[i + 1]) && IsHex(text[i + 2]))
            {
                bytes.Add((byte)((HexValue(text[i + 1]) << 4) | HexValue(text[i + 2])));
                i += 2;
                continue;
            }
            System.Text.Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed);
            var count = rune.EncodeToUtf8(utf8);
            for (var b = 0; b < count; b++) bytes.Add(utf8[b]);
            i += consumed - 1;
        }
        try
        {
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
        static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
    }

    /// <summary>Navigates <paramref name="node"/> by object keys and array indices.</summary>
    public static SourceNode? Navigate(SourceNode node, IEnumerable<string> segments)
    {
        var current = node;
        foreach (var segment in segments)
        {
            switch (current)
            {
                case SourceObject o when o.TryGetProperty(segment, out var child):
                    current = child;
                    break;
                case SourceArray a when IsArrayIndex(segment, out var index) && index < a.Items.Count:
                    current = a.Items[index];
                    break;
                default:
                    return null;
            }
        }
        return current;
    }

    private static bool IsArrayIndex(string segment, out int index)
    {
        index = -1;
        if (segment.Length == 0 || (segment.Length > 1 && segment[0] == '0')) return false;
        foreach (var c in segment)
        {
            if (c is < '0' or > '9') return false;
        }
        return int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }
}
