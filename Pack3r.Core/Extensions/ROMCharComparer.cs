using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.HighPerformance.Buffers;
using CommunityToolkit.HighPerformance.Helpers;

namespace Pack3r.Extensions;

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

        if (obj.Length <= 64)
        {
            return CombineHashcode(stackalloc char[64], obj.Span);
        }

        using var spanOwner = SpanOwner<char>.Allocate(obj.Length);
        return CombineHashcode(spanOwner.Span, obj.Span);

        static int CombineHashcode(scoped Span<char> buffer, ReadOnlySpan<char> value)
        {
            int count = value.ToLowerInvariant(buffer);
            return HashCode<char>.Combine(buffer[..count]);
        }
    }
}
