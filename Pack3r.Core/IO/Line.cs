using System.Diagnostics;
using Pack3r.Models;

namespace Pack3r.IO;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct Line : IEquatable<Line>, IResourceSource
{
    /// <summary>
    /// 1-based line index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Value of the line, with possibly comments and whitespace trimmed.
    /// </summary>
    public ReadOnlyMemory<char> Value { get; }

    /// <summary>
    /// The line as it was in the file, without comments or whitespace removed.
    /// Usually a slice of the whole file's text, so reading it doesn't allocate.
    /// </summary>
    public ReadOnlyMemory<char> RawMemory { get; }

    /// <inheritdoc cref="RawMemory"/>
    public ReadOnlySpan<char> RawSpan => RawMemory.Span;

    /// <summary>
    /// The line as it was in the file as a string, e.g. for error messages. Allocates, use
    /// <see cref="RawSpan"/> in parsing code.
    /// </summary>
    public string Raw => RawMemory.ToString();

    /// <summary>
    /// Path to the file where this line was read from.
    /// </summary>
    public string Path { get; }

    public bool HasValue => !Value.IsEmpty;

    /// <summary>
    /// First characters
    /// </summary>
    public char FirstChar { get; }

    public Line(string path, int index, string rawValue, bool keepRaw)
        : this(path, index, (rawValue ?? "").AsMemory(), keepRaw)
    {
    }

    public Line(string path, int index, ReadOnlyMemory<char> rawValue, bool keepRaw)
    {
        Path = path;
        Index = index;
        RawMemory = rawValue;

        if (keepRaw)
        {
            Value = rawValue;
        }
        else if (rawValue.Span.IndexOf("//", StringComparison.Ordinal) is int commentIndex and >= 0)
        {
            Value = rawValue[..commentIndex].Trim();
        }
        else
        {
            Value = rawValue.Trim();
        }

        if (!Value.IsEmpty)
        {
            FirstChar = Value.Span[0];
        }
    }

    public bool IsOpeningBrace => FirstChar == '{';
    public bool IsClosingBrace => FirstChar == '}';

    public override bool Equals(object? obj) => obj is Line other && Equals(other);

    public bool Equals(Line other)
    {
        return Index.Equals(other.Index)
            && RawSpan.SequenceEqual(other.RawSpan)
            && Path.Equals(other.Path);
    }

    public static bool operator ==(Line left, Line right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Line left, Line right)
    {
        return !(left == right);
    }

    public override int GetHashCode() => HashCode.Combine(Index, string.GetHashCode(RawSpan), Path);

    internal string DebuggerDisplay => $"{{ Line: '{Value}' in L{Index} {Path} }}";

    int IResourceSource.Position => Index;
    PositionType IResourceSource.Type => Index > 0 ? PositionType.Line : PositionType.Unknown;
    string IResourceSource.File => Path;
}
