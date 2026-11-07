using System.Globalization;
using BlazorDesignToolkit.Tokens.Diagnostics;

namespace BlazorDesignToolkit.Tokens.Json;

public enum SourceNodeKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null,
}

/// <summary>
/// Immutable, order-preserving JSON node that remembers where it came from.
/// Token values, extension data and documents are represented with these nodes so that
/// diagnostics can point to source locations and unknown data survives round-trips unchanged.
/// </summary>
public abstract class SourceNode
{
    private protected SourceNode(SourceLocation? location) => Location = location;

    public abstract SourceNodeKind Kind { get; }

    /// <summary>Location of the first character of this node, when parsed from text.</summary>
    public SourceLocation? Location { get; }

    /// <summary>Compares two nodes structurally (property order of objects is ignored, numbers compare by value).</summary>
    public static bool DeepEquals(SourceNode? a, SourceNode? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Kind != b.Kind) return false;
        switch (a)
        {
            case SourceString sa: return sa.Value == ((SourceString)b).Value;
            case SourceNumber na: return na.Value.Equals(((SourceNumber)b).Value);
            case SourceBoolean ba: return ba.Value == ((SourceBoolean)b).Value;
            case SourceNull: return true;
            case SourceArray aa:
                {
                    var ab = (SourceArray)b;
                    if (aa.Items.Count != ab.Items.Count) return false;
                    for (var i = 0; i < aa.Items.Count; i++)
                    {
                        if (!DeepEquals(aa.Items[i], ab.Items[i])) return false;
                    }
                    return true;
                }
            case SourceObject oa:
                {
                    var ob = (SourceObject)b;
                    if (oa.Count != ob.Count) return false;
                    foreach (var p in oa.Properties)
                    {
                        if (!ob.TryGetProperty(p.Name, out var other) || !DeepEquals(p.Value, other)) return false;
                    }
                    return true;
                }
            default: return false;
        }
    }

    /// <summary>Returns compact, deterministic JSON for this node (object properties in original order).</summary>
    public string ToJson(bool indented = false) => SourceJsonWriter.ToJsonString(this, indented);

    public override string ToString() => ToJson();
}

public sealed record SourceProperty(string Name, SourceNode Value, SourceLocation? NameLocation = null);

public sealed class SourceObject : SourceNode
{
    private readonly SourceProperty[] _properties;
    private readonly Dictionary<string, int> _index;

    public SourceObject(IEnumerable<SourceProperty> properties, SourceLocation? location = null) : base(location)
    {
        // Last occurrence wins for duplicate keys; the parser reports duplicates separately.
        var list = new List<SourceProperty>();
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in properties)
        {
            if (_index.TryGetValue(p.Name, out var existing))
            {
                list[existing] = p;
            }
            else
            {
                _index[p.Name] = list.Count;
                list.Add(p);
            }
        }
        _properties = [.. list];
    }

    public static SourceObject Empty { get; } = new([]);

    public static SourceObject Create(params (string Name, SourceNode Value)[] properties)
        => new(properties.Select(static p => new SourceProperty(p.Name, p.Value)));

    public override SourceNodeKind Kind => SourceNodeKind.Object;

    public IReadOnlyList<SourceProperty> Properties => _properties;

    public int Count => _properties.Length;

    public SourceNode? this[string name] => _index.TryGetValue(name, out var i) ? _properties[i].Value : null;

    public bool TryGetProperty(string name, out SourceNode value)
    {
        if (_index.TryGetValue(name, out var i))
        {
            value = _properties[i].Value;
            return true;
        }
        value = null!;
        return false;
    }

    public SourceProperty? GetPropertyEntry(string name) => _index.TryGetValue(name, out var i) ? _properties[i] : null;

    public bool ContainsKey(string name) => _index.ContainsKey(name);

    /// <summary>Returns a copy with <paramref name="name"/> set to <paramref name="value"/> (replacing in place, or appending).</summary>
    public SourceObject With(string name, SourceNode value)
    {
        var list = _properties.ToList();
        if (_index.TryGetValue(name, out var i)) list[i] = list[i] with { Value = value };
        else list.Add(new SourceProperty(name, value));
        return new SourceObject(list, Location);
    }

    public SourceObject Without(string name)
        => _index.ContainsKey(name) ? new SourceObject(_properties.Where(p => p.Name != name), Location) : this;
}

public sealed class SourceArray : SourceNode
{
    private readonly SourceNode[] _items;

    public SourceArray(IEnumerable<SourceNode> items, SourceLocation? location = null) : base(location) => _items = [.. items];

    public static SourceArray Create(params SourceNode[] items) => new(items);

    public override SourceNodeKind Kind => SourceNodeKind.Array;

    public IReadOnlyList<SourceNode> Items => _items;
}

public sealed class SourceString(string value, SourceLocation? location = null) : SourceNode(location)
{
    public override SourceNodeKind Kind => SourceNodeKind.String;
    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    public static implicit operator SourceString(string value) => new(value);
}

public sealed class SourceNumber : SourceNode
{
    public SourceNumber(double value, string? rawText = null, SourceLocation? location = null) : base(location)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
        Value = value;
        RawText = rawText;
    }

    public override SourceNodeKind Kind => SourceNodeKind.Number;
    public double Value { get; }
    /// <summary>The literal text from the source, kept so that serialization reproduces the author's formatting.</summary>
    public string? RawText { get; }

    public string ToInvariantString() => RawText ?? Value.ToString("R", CultureInfo.InvariantCulture);

    public static implicit operator SourceNumber(double value) => new(value);
}

public sealed class SourceBoolean(bool value, SourceLocation? location = null) : SourceNode(location)
{
    public override SourceNodeKind Kind => SourceNodeKind.Boolean;
    public bool Value { get; } = value;
}

public sealed class SourceNull(SourceLocation? location = null) : SourceNode(location)
{
    public static SourceNull Instance { get; } = new();
    public override SourceNodeKind Kind => SourceNodeKind.Null;
}
