using System.Diagnostics;
using Pack3r.Models;

namespace Pack3r.IO;

/// <summary>
/// A line kept after parsing, e.g. as the source of a resource for log messages. A copy of the line;
/// created from a <see cref="LineRef"/> with <see cref="LineRef.ToLine"/> only when it's needed.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct Line : IEquatable<Line>, IResourceSource
{
    private readonly string? _raw;
    private readonly int _valueStart;
    private readonly int _valueLength;

    internal Line(string path, int index, string raw, int valueStart, int valueLength)
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
        string raw = rawValue ?? "";
        var line = new LineRef(path, index, raw, keepRaw);
        this = new Line(path, index, raw, line.ValueStart, line.Value.Length);
    }

    /// <summary>1-based line number.</summary>
    public int Index { get; }

    /// <summary>Path to the file the line was read from.</summary>
    public string Path { get; }

    /// <summary>The line as it was in the file.</summary>
    public ReadOnlySpan<char> RawSpan => _raw;

    /// <summary>The line with comments and whitespace removed, unless read as raw.</summary>
    public ReadOnlySpan<char> Value => _raw.AsSpan(_valueStart, _valueLength);

    public bool HasValue => _valueLength > 0;

    /// <summary>First character of <see cref="Value"/>, or 0 if it's empty.</summary>
    public char FirstChar => HasValue ? _raw![_valueStart] : '\0';

    /// <summary>The raw line, for log and error messages.</summary>
    public string Raw => _raw ?? "";

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
        hash.Add(string.GetHashCode(RawSpan));
        hash.Add(Path, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    internal string DebuggerDisplay => $"{{ Line: '{Value}' in L{Index} {Path} }}";

    int IResourceSource.Position => Index;
    PositionType IResourceSource.Type => Index > 0 ? PositionType.Line : PositionType.Unknown;
    string IResourceSource.File => Path;
}
