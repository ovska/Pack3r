using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.HighPerformance.Helpers;

namespace Pack3r;

public sealed class ROMCharComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
    public static readonly ROMCharComparer Instance = new();

    private ROMCharComparer() { }

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return x.Length == y.Length && x.Span.SequenceEqual(y.Span);
    }

    public int GetHashCode([DisallowNull] ReadOnlyMemory<char> obj)
    {
        return obj.IsEmpty ? 0 : HashCode<char>.Combine(obj.Span);
    }
}
