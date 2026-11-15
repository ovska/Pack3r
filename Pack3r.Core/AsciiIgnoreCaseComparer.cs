using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pack3r.Extensions;

namespace Pack3r;

/// <summary>
/// Compares ASCII strings ignoring case, and allows looking them up by their bytes without decoding them.
/// </summary>
/// <remarks>
/// <see cref="QString.Comparer"/>'s hash can't be computed from bytes, so this uses its own. Assumes the values are
/// valid ASCII: a value with other characters doesn't match anything but itself.
/// </remarks>
public sealed class AsciiIgnoreCaseComparer :
    IEqualityComparer<QString>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, QString>
{
    public static AsciiIgnoreCaseComparer Instance { get; } = new();

    private const int StackLimit = 256;

    private AsciiIgnoreCaseComparer() { }

    public bool Equals(QString x, QString y) => Ascii.EqualsIgnoreCase(x.Span, y.Span);

    public bool Equals(ReadOnlySpan<byte> alternate, QString other) => Ascii.EqualsIgnoreCase(alternate, other.Span);

    public int GetHashCode([DisallowNull] QString obj)
    {
        // narrowed to bytes to hash them the same way; only done when a string is added to the set
        ReadOnlySpan<char> chars = obj.Span;
        byte[]? rented = null;
        Span<byte> bytes = chars.Length <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(chars.Length));

        _ = Ascii.FromUtf16(chars, bytes, out int written);
        int hash = GetHashCode(bytes[..written]);

        if (rented is not null)
            ArrayPool<byte>.Shared.Return(rented);

        return hash;
    }

    public int GetHashCode(ReadOnlySpan<byte> alternate)
    {
        ref byte start = ref MemoryMarshal.GetReference(alternate);
        int length = alternate.Length;
        ulong hash = (ulong)length;
        int i = 0;

        for (; i <= length - sizeof(ulong); i += sizeof(ulong))
            hash = Mix(hash, ToLower(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref start, i))));

        if (i < length)
        {
            // the rest zero-padded, which lowercasing leaves as is
            ulong tail = 0;
            alternate[i..].CopyTo(MemoryMarshal.AsBytes(new Span<ulong>(ref tail)));
            hash = Mix(hash, ToLower(tail));
        }

        return (int)(hash ^ (hash >> 32));
    }

    public QString Create(ReadOnlySpan<byte> alternate) => alternate.ToQString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong hash, ulong value) => BitOperations.RotateLeft((hash ^ value) * 0x9E3779B97F4A7C15, 29);

    /// <summary>Lowercases 8 ASCII bytes at once without branching.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong ToLower(ulong value)
    {
        const ulong Ones = 0x0101010101010101;

        // for bytes under 0x80 the additions don't carry into the next byte, and set its high bit when it's
        // at least 'A' and when it's past 'Z', so the high bits that differ mark the uppercase letters
        ulong atLeastA = value + (0x80 - 'A') * Ones;
        ulong pastZ = value + (0x80 - 'Z' - 1) * Ones;
        ulong upper = (atLeastA ^ pastZ) & (0x80 * Ones);

        // the high bit shifted to 0x20, the case bit
        return value | (upper >> 2);
    }
}
