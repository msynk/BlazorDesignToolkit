using System.Collections.Immutable;

namespace BlazorDesignToolkit.Tokens;

/// <summary>
/// Immutable path of a token or group, e.g. <c>color.text.primary</c>. Segments are case-sensitive and compared ordinally.
/// The reserved <c>$root</c> segment is kept as part of the path, as required by DTCG 2025.10 §6.7.2.
/// </summary>
public readonly struct TokenPath : IEquatable<TokenPath>, IComparable<TokenPath>
{
    public const string RootTokenName = "$root";

    private readonly ImmutableArray<string> _segments;

    public TokenPath(IEnumerable<string> segments) => _segments = [.. segments];

    private TokenPath(ImmutableArray<string> segments) => _segments = segments;

    public static TokenPath Empty { get; } = new(ImmutableArray<string>.Empty);

    public ImmutableArray<string> Segments => _segments.IsDefault ? [] : _segments;

    public int Length => Segments.Length;

    public bool IsEmpty => Length == 0;

    public string Name => Length == 0 ? "" : Segments[^1];

    public TokenPath Parent => Length <= 1 ? Empty : new TokenPath(Segments.RemoveAt(Length - 1));

    /// <summary>True when the last segment is <c>$root</c>.</summary>
    public bool IsRootToken => Length > 0 && Segments[^1] == RootTokenName;

    public TokenPath Append(string segment) => new(Segments.Add(segment));

    public TokenPath Append(TokenPath relative) => new(Segments.AddRange(relative.Segments));

    public bool StartsWith(TokenPath prefix)
    {
        if (prefix.Length > Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(Segments[i], prefix.Segments[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public TokenPath RelativeTo(TokenPath prefix)
        => StartsWith(prefix) ? new TokenPath(Segments.RemoveRange(0, prefix.Length)) : throw new ArgumentException("Path is not under the prefix.", nameof(prefix));

    /// <summary>Parses a dot-separated path. Names cannot contain '.', so splitting is unambiguous.</summary>
    public static TokenPath Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length == 0 ? Empty : new TokenPath(path.Split('.'));
    }

    public override string ToString() => string.Join('.', Segments);

    public bool Equals(TokenPath other)
    {
        if (Length != other.Length) return false;
        for (var i = 0; i < Length; i++)
        {
            if (!string.Equals(Segments[i], other.Segments[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is TokenPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var s in Segments) hash.Add(s, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public int CompareTo(TokenPath other)
    {
        var n = Math.Min(Length, other.Length);
        for (var i = 0; i < n; i++)
        {
            var c = string.CompareOrdinal(Segments[i], other.Segments[i]);
            if (c != 0) return c;
        }
        return Length.CompareTo(other.Length);
    }

    public static bool operator ==(TokenPath left, TokenPath right) => left.Equals(right);
    public static bool operator !=(TokenPath left, TokenPath right) => !left.Equals(right);
    public static bool operator <(TokenPath left, TokenPath right) => left.CompareTo(right) < 0;
    public static bool operator >(TokenPath left, TokenPath right) => left.CompareTo(right) > 0;
    public static bool operator <=(TokenPath left, TokenPath right) => left.CompareTo(right) <= 0;
    public static bool operator >=(TokenPath left, TokenPath right) => left.CompareTo(right) >= 0;

    public static implicit operator TokenPath(string path) => Parse(path);
}
