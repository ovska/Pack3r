using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.HighPerformance.Helpers;

namespace Pack3r;

public sealed class ROMCharComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
    public static readonly ROMCharComparer Instance = new();

    private ROMCharComparer() { }

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return x.Length == y.Length && x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode([DisallowNull] ReadOnlyMemory<char> obj)
    {
        if (obj.IsEmpty)
            return 0;

        scoped Span<char> dst = obj.Length < 64
            ? stackalloc char[64]
            : new char[64];

        int count = obj.Span.ToLowerInvariant(dst);

        return HashCode<char>.Combine(dst[..count]);
    }
}
