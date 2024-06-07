using Pack3r.Extensions;

namespace Pack3r.IO;

public readonly struct Line : IEquatable<Line>
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
    /// Raw value of the line with whitespace and comments removed.
    /// </summary>
    public string Raw { get; }

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
    {
        Path = path;
        Index = index;
        Raw = rawValue ?? "";
        Value = keepRaw ? Raw.AsMemory() : Raw.AsMemory().Trim();

        if (keepRaw)
        {
            Value = Raw.AsMemory();
        }
        else if (Raw.IndexOf("//") is int commentIndex and >= 0)
        {
            Value = Raw.AsMemory(0, commentIndex).Trim();
        }
        else
        {
            Value = Raw.AsMemory().Trim();
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
            && Raw.Equals(other.Raw)
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

    public override int GetHashCode() => HashCode.Combine(Index, Raw, Path);
}
