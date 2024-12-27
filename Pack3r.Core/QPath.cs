using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pack3r;

public readonly struct QPath :
    IEquatable<QPath>,
    IComparable<QPath>,
    IEquatable<string>,
    ISpanFormattable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlySpan<char>(QPath path) => path.Span;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlyMemory<char>(QPath path) => path.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QPath(string path) => new(path);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QPath(ReadOnlyMemory<char> path) => new(path);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QPath(QString path) => new(path.Value);

    public ReadOnlyMemory<char> Value { get; }
    public ReadOnlySpan<char> Span => Value.Span;

    public char this[int index] => Span[index];

    public QPath this[Range range] => new(Value, range);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QPath(string path)
    {
        Global.EnsureQPathLength(path);
        Value = path.Replace('\\', '/').AsMemory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QPath(ReadOnlyMemory<char> path)
    {
        Global.EnsureQPathLength(path);

        if (path.Span.Contains('\\'))
        {
            Value = string.Create(path.Length, path, (dst, src) => src.Span.Replace(dst, '\\', '/')).AsMemory();
        }
        else
        {
            Value = path;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private QPath(ReadOnlyMemory<char> value, Range range)
    {
        Debug.Assert(!value.Span.Contains('\\'), $"Non-sanitized qpath: {value}");
        Value = value[range];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(QPath other) => CultureInfo.InvariantCulture.CompareInfo.Compare(Value.Span, other.Value.Span);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(QPath other) => Value.Span.Equals(other.Value.Span, StringComparison.OrdinalIgnoreCase);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => CultureInfo.InvariantCulture.CompareInfo.GetHashCode(Value.Span, CompareOptions.OrdinalIgnoreCase);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching", Justification = "<Pending>")]
    public override bool Equals(object? obj) => obj is QPath && Equals((QPath)obj);

    public override string ToString() => Value.ToString();

    public bool Equals(string? other) => other.AsSpan().Equals(Span, StringComparison.OrdinalIgnoreCase);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (Value.Span.TryCopyTo(destination))
        {
            charsWritten = Value.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => Value.ToString();

    public static bool operator ==(QPath left, QPath right) => left.Equals(right);
    public static bool operator !=(QPath left, QPath right) => !(left == right);
    public static bool operator <(QPath left, QPath right) => left.CompareTo(right) < 0;
    public static bool operator <=(QPath left, QPath right) => left.CompareTo(right) <= 0;
    public static bool operator >(QPath left, QPath right) => left.CompareTo(right) > 0;
    public static bool operator >=(QPath left, QPath right) => left.CompareTo(right) >= 0;
}
