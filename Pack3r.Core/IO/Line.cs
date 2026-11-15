using System.Diagnostics;
using System.Text;
using Pack3r.Extensions;
using Pack3r.Models;

namespace Pack3r.IO;

/// <summary>
/// A line kept after parsing, e.g. as the source of a resource for log messages. A copy of the line's ASCII
/// bytes; created from a <see cref="LineRef"/> with <see cref="LineRef.ToLine"/> only when it's needed.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct Line : IEquatable<Line>, IResourceSource
{
    private readonly byte[]? _raw;
    private readonly int _valueStart;
    private readonly int _valueLength;

    internal Line(string path, int index, byte[] raw, int valueStart, int valueLength)
    {
        Path = path;
        Index = index;
        _raw = raw;
        _valueStart = valueStart;
        _valueLength = valueLength;
    }

    /// <summary>
    /// Creates a line from text, e.g. for a resource that doesn't come from a parsed file.
    /// </summary>
    public Line(string path, int index, string rawValue, bool keepRaw)
    {
        byte[] raw = Encoding.ASCII.GetBytes(rawValue ?? "");
        var line = new LineRef(path, index, raw, keepRaw);
        this = line.ToLine();
    }

    /// <summary>1-based line number.</summary>
    public int Index { get; }

    /// <summary>Path to the file the line was read from.</summary>
    public string Path { get; }

    /// <summary>The line as it was in the file.</summary>
    public ReadOnlySpan<byte> RawSpan => _raw;

    /// <summary>The line with comments and whitespace removed, unless read as raw.</summary>
    public ReadOnlySpan<byte> Value => _raw.AsSpan(_valueStart, _valueLength);

    public bool HasValue => _valueLength > 0;

    /// <summary>First byte of <see cref="Value"/>, or 0 if it's empty.</summary>
    public byte FirstChar => HasValue ? _raw![_valueStart] : (byte)0;

    /// <summary>The raw line as a string, for log and error messages. Allocates.</summary>
    public string Raw => RawSpan.ToAsciiString();

    public override bool Equals(object? obj) => obj is Line other && Equals(other);

    public bool Equals(Line other)
    {
        return Index == other.Index
            && RawSpan.SequenceEqual(other.RawSpan)
            && string.Equals(Path, other.Path, StringComparison.Ordinal);
    }

    public static bool operator ==(Line left, Line right) => left.Equals(right);
    public static bool operator !=(Line left, Line right) => !(left == right);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Index);
        hash.AddBytes(RawSpan);
        hash.Add(Path, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    internal string DebuggerDisplay => $"{{ Line: '{Value.ToAsciiString()}' in L{Index} {Path} }}";

    int IResourceSource.Position => Index;
    PositionType IResourceSource.Type => Index > 0 ? PositionType.Line : PositionType.Unknown;
    string IResourceSource.File => Path;
}
