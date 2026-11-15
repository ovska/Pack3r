using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Pack3r.Extensions;

namespace Pack3r;

/// <summary>
/// Compares strings case-insensitively like <see cref="QString.Comparer"/>, and allows looking them up by their
/// ASCII bytes without decoding them. Bytes outside ASCII match '?', which is what they become in a string, see
/// <see cref="AsciiExtensions.ToAsciiString"/>.
/// </summary>
/// <remarks>
/// <see cref="QString.Comparer"/>'s hash can't be computed from bytes, so this uses its own: only use it for a
/// set of strings that were decoded from ASCII, where it gives the same results.
/// </remarks>
public sealed class AsciiIgnoreCaseComparer :
    IEqualityComparer<QString>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, QString>
{
    public static AsciiIgnoreCaseComparer Instance { get; } = new();

    private AsciiIgnoreCaseComparer() { }

    public bool Equals(QString x, QString y) => x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);

    public bool Equals(ReadOnlySpan<byte> alternate, QString other)
    {
        // vectorized, but false for any byte outside ASCII
        if (Ascii.EqualsIgnoreCase(alternate, other.Span))
            return true;

        if (alternate.Length != other.Length || Ascii.IsValid(alternate))
            return false;

        ReadOnlySpan<char> chars = other.Span;

        for (int i = 0; i < alternate.Length; i++)
        {
            if (Fold(alternate[i]) != Fold(chars[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode([DisallowNull] QString obj)
    {
        // FNV-1a
        uint hash = 2166136261;

        foreach (char c in obj.Span)
            hash = (hash ^ Fold(c)) * 16777619;

        return (int)hash;
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        uint hash = 2166136261;

        foreach (byte b in alternate)
            hash = (hash ^ Fold(b)) * 16777619;

        return (int)hash;
    }

    public QString Create(ReadOnlySpan<byte> alternate) => alternate.ToQString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Fold(uint value) => value switch
    {
        >= 'A' and <= 'Z' => value | 0x20,
        > 0x7F => '?',
        _ => value,
    };
}
