using System.Globalization;
using System.Runtime.CompilerServices;

namespace Pack3r;

public readonly struct QString : IEquatable<QString>, IComparable<QString>, IEquatable<string>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlySpan<char>(QString value) => value.Span;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlyMemory<char>(QString value) => value.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(string value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(ReadOnlyMemory<char> value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(QPath value) => new(value.Value);

    public ReadOnlyMemory<char> Value { get; }
    public ReadOnlySpan<char> Span => Value.Span;

    public char this[int index] => Span[index];

    public QString this[Range range] => new(Value, range);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QString(string value)
    {
        Value = value.AsMemory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QString(ReadOnlyMemory<char> value)
    {
        Value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private QString(ReadOnlyMemory<char> value, Range range)
    {
        Value = value[range];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(QString other) => CultureInfo.InvariantCulture.CompareInfo.Compare(Value.Span, other.Value.Span);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(QString other) => Value.Span.Equals(other.Value.Span, StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => CultureInfo.InvariantCulture.CompareInfo.GetHashCode(Value.Span, CompareOptions.OrdinalIgnoreCase);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0038:Use pattern matching", Justification = "<Pending>")]
    public override bool Equals(object? obj) => obj is QString && Equals((QString)obj);

    public override string ToString() => Value.ToString();

    public bool Equals(string? other) => other.AsSpan().Equals(Span, StringComparison.OrdinalIgnoreCase);

    public static bool operator ==(QString left, QString right) => left.Equals(right);
    public static bool operator !=(QString left, QString right) => !(left == right);
    public static bool operator <(QString left, QString right) => left.CompareTo(right) < 0;
    public static bool operator <=(QString left, QString right) => left.CompareTo(right) <= 0;
    public static bool operator >(QString left, QString right) => left.CompareTo(right) > 0;
    public static bool operator >=(QString left, QString right) => left.CompareTo(right) >= 0;
}
