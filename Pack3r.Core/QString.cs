using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;

namespace Pack3r;

public readonly struct QString :
    IEquatable<QString>,
    IComparable<QString>,
    IEquatable<string>,
    ISpanFormattable
{
    public static readonly ComparerImpl Comparer = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlySpan<char>(QString value) => value.Span;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator ReadOnlyMemory<char>(QString value) => value.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(string value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(ReadOnlyMemory<char> value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator QString(QPath value) => new(value.Value);

    public ReadOnlyMemory<char> Value { get; }
    public ReadOnlySpan<char> Span => Value.Span;

    public bool IsEmpty => Value.IsEmpty;
    public int Length => Value.Length;

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

    public override bool Equals(object? obj) => obj is QString qstring && Equals(qstring);

    public override string ToString() => Value.ToString();

    public bool Equals(string? other) => other.AsSpan().Equals(Span, StringComparison.OrdinalIgnoreCase);

    public QString TrimTextureExtension() => Value.Span.GetTextureExtension() switch
    {
        TextureExtension.Tga or TextureExtension.Jpg => new QString(Value[..^4]),
        _ => this,
    };

    public QString TrimExtension()
    {
        ReadOnlySpan<char> span = Value.Span;

        if (Path.GetExtension(span) is { Length: > 0 } extension)
        {
            return new QString(Value[..^extension.Length]);
        }

        return this;
    }

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

    public static bool operator ==(QString left, QString right) => left.Equals(right);
    public static bool operator !=(QString left, QString right) => !(left == right);
    public static bool operator <(QString left, QString right) => left.CompareTo(right) < 0;
    public static bool operator <=(QString left, QString right) => left.CompareTo(right) <= 0;
    public static bool operator >(QString left, QString right) => left.CompareTo(right) > 0;
    public static bool operator >=(QString left, QString right) => left.CompareTo(right) >= 0;

    public sealed class ComparerImpl :
        IEqualityComparer<QString>,
        IAlternateEqualityComparer<ReadOnlySpan<char>, QString>,
        IAlternateEqualityComparer<ReadOnlyMemory<char>, QString>
    {
        public QString Create(ReadOnlySpan<char> alternate) => new(alternate.ToString());
        public QString Create(ReadOnlyMemory<char> alternate) => new(alternate);
        public bool Equals(ReadOnlySpan<char> alternate, QString other) => alternate.Equals(other.Value.Span, StringComparison.OrdinalIgnoreCase);
        public bool Equals(QString x, QString y) => x.Value.Span.Equals(y.Value.Span, StringComparison.OrdinalIgnoreCase);
        public bool Equals(ReadOnlyMemory<char> alternate, QString other) => alternate.Span.Equals(other.Span, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(ReadOnlySpan<char> alternate) => CultureInfo.InvariantCulture.CompareInfo.GetHashCode(alternate, CompareOptions.OrdinalIgnoreCase);
        public int GetHashCode([DisallowNull] QString obj) => CultureInfo.InvariantCulture.CompareInfo.GetHashCode(obj.Span, CompareOptions.OrdinalIgnoreCase);
        public int GetHashCode(ReadOnlyMemory<char> alternate) => CultureInfo.InvariantCulture.CompareInfo.GetHashCode(alternate.Span, CompareOptions.OrdinalIgnoreCase);
    }
}
